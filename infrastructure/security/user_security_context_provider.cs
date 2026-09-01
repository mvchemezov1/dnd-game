#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using dnd_game.application.security;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// Строит <see cref="UserSecurityContext"/> для текущего аутентифицированного HTTP-запроса.
    /// Использует реального пользователя из <see cref="ICurrentUserService"/> и данные из
    /// <see cref="IUserRepository"/> и <see cref="ICharacterOwnershipRepository"/>.
    /// Результат кэшируется в <see cref="HttpContext.Items"/> на время одного запроса,
    /// чтобы не выполнять повторные обращения к базе при множественных проверках прав.
    /// </summary>
    public sealed class HttpUserSecurityContextProvider(
        IHttpContextAccessor httpContextAccessor,
        ICurrentUserService currentUserService,
        IUserRepository userRepository,
        ICharacterOwnershipRepository ownershipRepository) : IUserSecurityContextProvider
    {
        private const string CacheKey = "__UserSecurityContext";

        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        private readonly IUserRepository _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        private readonly ICharacterOwnershipRepository _ownershipRepository = ownershipRepository ?? throw new ArgumentNullException(nameof(ownershipRepository));

        /// <inheritdoc />
        public async Task<UserSecurityContext> GetCurrentContextAsync(CancellationToken cancellationToken = default)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // Проверяем кэш текущего запроса
            if (httpContext != null &&
                httpContext.Items.TryGetValue(CacheKey, out var cached) &&
                cached is UserSecurityContext cachedContext)
            {
                return cachedContext;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var userId = _currentUserService.GetCurrentUserId();
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Аутентифицированный пользователь не найден в базе данных.");

            var context = new UserSecurityContext
            {
                UserId = userId,
                GlobalRole = user.GlobalRole,
                OwnedCharacterIds = await _ownershipRepository
                    .GetOwnedCharacterIdsAsync(userId, cancellationToken)
                    .ConfigureAwait(false),
                CampaignRoles = user.CampaignRoles ?? []
            };

            // Кэшируем на время обработки текущего запроса
            if (httpContext != null)
            {
                httpContext.Items[CacheKey] = context;
            }

            return context;
        }
    }
}