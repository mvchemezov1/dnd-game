#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.security;

namespace dnd_game.Infrastructure.Security
{
    /// <summary>
    /// Управляемый фейковый провайдер контекста безопасности для юнит- и интеграционных тестов.
    /// Позволяет задавать фиксированные значения пользователя, роли, персонажей и ролей в кампаниях.
    /// </summary>
    public sealed class FakeUserSecurityContextProvider : IUserSecurityContextProvider
    {
        private readonly Guid _userId;
        private readonly UserRole _globalRole;
        private readonly List<Guid> _ownedCharacterIds;
        private readonly Dictionary<Guid, CampaignRole> _campaignRoles;

        /// <summary>
        /// Создаёт экземпляр фейкового провайдера.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя. Если не задан, генерируется случайный.</param>
        /// <param name="globalRole">Глобальная роль пользователя (по умолчанию Player).</param>
        /// <param name="ownedCharacterIds">Список идентификаторов персонажей, принадлежащих пользователю.</param>
        /// <param name="campaignRoles">Словарь ролей пользователя в кампаниях (ключ — campaignId).</param>
        /// <exception cref="ArgumentException">Если <paramref name="userId"/> равен Guid.Empty.</exception>
        public FakeUserSecurityContextProvider(
            Guid? userId = null,
            UserRole globalRole = UserRole.Player,
            List<Guid>? ownedCharacterIds = null,
            Dictionary<Guid, CampaignRole>? campaignRoles = null)
        {
            _userId = userId ?? Guid.NewGuid();
            if (_userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(userId));

            _globalRole = globalRole;
            _ownedCharacterIds = ownedCharacterIds ?? [];
            _campaignRoles = campaignRoles ?? [];
        }

        /// <inheritdoc />
        public Task<UserSecurityContext> GetCurrentContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var context = new UserSecurityContext
            {
                UserId = _userId,
                GlobalRole = _globalRole,
                OwnedCharacterIds = _ownedCharacterIds,
                CampaignRoles = _campaignRoles
            };

            return Task.FromResult(context);
        }
    }
}