using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Security.Claims;

namespace dnd_game.application.security
{
    /// <summary>
    /// Сервис для получения идентификатора текущего аутентифицированного пользователя.
    /// </summary>
    public class CurrentUserService(IHttpContextAccessor httpContextAccessor, ILogger<CurrentUserService>? logger = null) : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        private readonly ILogger<CurrentUserService> _logger = logger ?? NullLogger<CurrentUserService>.Instance;

        /// <summary>
        /// Возвращает идентификатор текущего аутентифицированного пользователя.
        /// Если пользователь не аутентифицирован или идентификатор некорректен, выбрасывает <see cref="UnauthorizedAccessException"/>.
        /// </summary>
        /// <returns>GUID идентификатор пользователя.</returns>
        public Guid GetCurrentUserId()
        {
            var userId = TryGetCurrentUserId();
            if (userId == null)
            {
                _logger.LogWarning("Попытка получить идентификатор пользователя без аутентификации.");
                throw new UnauthorizedAccessException("Текущий пользователь не аутентифицирован.");
            }
            return userId.Value;
        }

        /// <summary>
        /// Мягкий вариант получения идентификатора текущего пользователя.
        /// Возвращает <c>null</c>, если пользователь не аутентифицирован или идентификатор некорректен.
        /// </summary>
        /// <returns>GUID идентификатор пользователя или <c>null</c>.</returns>
        public Guid? TryGetCurrentUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var userIdClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(userIdClaim))
                return null;

            if (Guid.TryParse(userIdClaim, out var userId))
                return userId;

            _logger.LogWarning("Некорректный идентификатор пользователя в Claim: {ClaimValue}", userIdClaim);
            return null;
        }
    }
}