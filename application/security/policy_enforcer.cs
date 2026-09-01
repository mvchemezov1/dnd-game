using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.security
{
    /// <summary>
    /// Применяет политики безопасности к операциям пользователя.
    /// Выбрасывает <see cref="UnauthorizedAccessException"/> при отсутствии прав.
    /// </summary>
    public class PolicyEnforcer(
        PermissionChecker checker,
        ICurrentUserService currentUser,
        ILogger<PolicyEnforcer>? logger = null)
    {
        private readonly PermissionChecker _checker = checker ?? throw new ArgumentNullException(nameof(checker));
        private readonly ICurrentUserService _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        private readonly ILogger<PolicyEnforcer> _logger = logger ?? NullLogger<PolicyEnforcer>.Instance;

        /// <summary>
        /// Проверяет, что указанный идентификатор пользователя совпадает с текущим аутентифицированным пользователем.
        /// </summary>
        private void EnsureCurrentUserMatches(Guid userId)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не должен быть пустым.", nameof(userId));

            var currentUserId = _currentUser.GetCurrentUserId();
            if (currentUserId != userId)
            {
                _logger.LogWarning("Попытка выполнить операцию от имени другого пользователя. Текущий: {CurrentUserId}, запрошенный: {UserId}", currentUserId, userId);
                throw new UnauthorizedAccessException("Несоответствие пользователя: операция запрещена.");
            }
        }

        private static void EnsureCharacterIdValid(Guid characterId)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не должен быть пустым.", nameof(characterId));
        }

        private static void EnsureCampaignIdValid(Guid campaignId)
        {
            if (campaignId == Guid.Empty)
                throw new ArgumentException("Идентификатор кампании не должен быть пустым.", nameof(campaignId));
        }

        private static void EnsureCombatIdValid(Guid combatId)
        {
            if (combatId == Guid.Empty)
                throw new ArgumentException("Идентификатор боя не должен быть пустым.", nameof(combatId));
        }

        private static void EnsureNpcIdValid(Guid npcId)
        {
            if (npcId == Guid.Empty)
                throw new ArgumentException("Идентификатор NPC не должен быть пустым.", nameof(npcId));
        }

        // ---------- Персонажи ----------

        /// <summary>
        /// Требует право на просмотр персонажа.
        /// </summary>
        public async Task EnforceViewCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureCharacterIdValid(characterId);
            if (!await _checker.CanViewCharacterAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для просмотра этого персонажа.");
        }

        /// <summary>
        /// Требует право на редактирование персонажа.
        /// </summary>
        public async Task EnforceEditCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureCharacterIdValid(characterId);
            if (!await _checker.CanEditCharacterAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для редактирования этого персонажа.");
        }

        /// <summary>
        /// Требует право на управление персонажем.
        /// </summary>
        public async Task EnforceControlCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureCharacterIdValid(characterId);
            if (!await _checker.CanControlCharacterAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для управления этим персонажем.");
        }

        /// <summary>
        /// Требует право на удаление персонажа.
        /// </summary>
        public async Task EnforceDeleteCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureCharacterIdValid(characterId);
            if (!await _checker.CanDeleteCharacterAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для удаления этого персонажа.");
        }

        /// <summary>
        /// Требует право на управление инвентарём персонажа.
        /// </summary>
        public async Task EnforceManageInventoryAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureCharacterIdValid(characterId);
            if (!await _checker.CanManageInventoryAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для управления инвентарём этого персонажа.");
        }

        /// <summary>
        /// Требует право на использование заклинаний от имени персонажа.
        /// </summary>
        public async Task EnforceCastSpellAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureCharacterIdValid(characterId);
            if (!await _checker.CanCastSpellAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для использования заклинаний этим персонажем.");
        }

        /// <summary>
        /// Требует право на выполнение проверок навыков от имени персонажа.
        /// </summary>
        public async Task EnforcePerformSkillCheckAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureCharacterIdValid(characterId);
            if (!await _checker.CanPerformSkillCheckAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для выполнения проверок навыков этим персонажем.");
        }

        // ---------- Кампании ----------

        /// <summary>
        /// Требует право на просмотр кампании.
        /// </summary>
        public async Task EnforceViewCampaignAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureCampaignIdValid(campaignId);
            if (!await _checker.CanViewCampaignAsync(campaignId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для просмотра этой кампании.");
        }

        /// <summary>
        /// Требует право на редактирование кампании.
        /// </summary>
        public async Task EnforceEditCampaignAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureCampaignIdValid(campaignId);
            if (!await _checker.CanEditCampaignAsync(campaignId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для редактирования этой кампании.");
        }

        /// <summary>
        /// Требует право на начало боя в кампании.
        /// </summary>
        public async Task EnforceStartCombatAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureCampaignIdValid(campaignId);
            if (!await _checker.CanStartCombatAsync(campaignId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для начала боя в этой кампании.");
        }

        /// <summary>
        /// Требует право на завершение боя.
        /// </summary>
        public async Task EnforceEndCombatAsync(Guid combatId, CancellationToken ct = default)
        {
            EnsureCombatIdValid(combatId);
            if (!await _checker.CanEndCombatAsync(combatId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для завершения этого боя.");
        }

        /// <summary>
        /// Требует право на отправку сообщений в кампанию.
        /// </summary>
        public async Task EnforceSendMessageToCampaignAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureCampaignIdValid(campaignId);
            if (!await _checker.CanSendMessageToCampaignAsync(campaignId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для отправки сообщений в этой кампании.");
        }

        // ---------- Мастерские действия ----------

        /// <summary>
        /// Требует роль Мастера (или администратора).
        /// </summary>
        public async Task EnforceGameMasterActionAsync(CancellationToken ct = default)
        {
            if (!await _checker.IsGameMasterAsync(ct))
                throw new UnauthorizedAccessException("Только Мастер может выполнить это действие.");
        }

        /// <summary>
        /// Требует роль администратора.
        /// </summary>
        public async Task EnforceAdminActionAsync(CancellationToken ct = default)
        {
            if (!await _checker.IsAdminAsync(ct))
                throw new UnauthorizedAccessException("Только администратор может выполнить это действие.");
        }

        /// <summary>
        /// Требует право на управление NPC.
        /// </summary>
        public async Task EnforceManageNpcAsync(Guid npcId, CancellationToken ct = default)
        {
            EnsureNpcIdValid(npcId);
            if (!await _checker.CanManageNpcAsync(npcId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для управления этим NPC.");
        }

        // ---------- Прочее ----------

        /// <summary>
        /// Требует право на бросок костей.
        /// </summary>
        public async Task EnforceRollDiceAsync(CancellationToken ct = default)
        {
            if (!await _checker.CanRollDiceAsync(ct))
                throw new UnauthorizedAccessException("Бросок костей в данный момент недоступен.");
        }

        /// <summary>
        /// Перегрузка для совместимости: проверяет, что указанный userId совпадает с текущим пользователем,
        /// после чего проверяет право на редактирование персонажа.
        /// </summary>
        public async Task EnforceEditCharacterAsync(Guid userId, Guid characterId, CancellationToken ct = default)
        {
            EnsureCurrentUserMatches(userId);
            await EnforceEditCharacterAsync(characterId, ct);
        }
    }
}