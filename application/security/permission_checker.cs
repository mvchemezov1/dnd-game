using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.security
{
    /// <summary>
    /// Роли пользователей в системе DnD.
    /// </summary>
    public enum UserRole
    {
        Player = 1,
        GameMaster = 2,
        Admin = 3
    }

    /// <summary>
    /// Роль пользователя в конкретной кампании.
    /// </summary>
    public enum CampaignRole
    {
        Player,
        GameMaster,
        Spectator
    }

    /// <summary>
    /// Информация о пользователе для проверок безопасности.
    /// </summary>
    public class UserSecurityContext
    {
        public Guid UserId { get; set; }
        public UserRole GlobalRole { get; set; } = UserRole.Player;
        /// <summary>
        /// Список идентификаторов персонажей, которыми владеет пользователь (как игрок).
        /// </summary>
        public List<Guid> OwnedCharacterIds { get; set; } = [];
        /// <summary>
        /// Роль пользователя в каждой из кампаний, где он состоит.
        /// </summary>
        public Dictionary<Guid, CampaignRole> CampaignRoles { get; set; } = [];
    }

    /// <summary>
    /// Интерфейс получения контекста безопасности текущего пользователя.
    /// </summary>
    public interface IUserSecurityContextProvider
    {
        Task<UserSecurityContext> GetCurrentContextAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Репозиторий для получения дополнительной информации о персонажах (владелец, статус, кампания).
    /// </summary>
    public interface ICharacterOwnershipRepository
    {
        Task<Guid?> GetOwnerIdAsync(Guid characterId, CancellationToken cancellationToken = default);
        Task<Guid?> GetCampaignIdAsync(Guid characterId, CancellationToken cancellationToken = default);
        Task<bool> IsNonPlayerCharacterAsync(Guid characterId, CancellationToken cancellationToken = default);
        Task<List<Guid>> GetOwnedCharacterIdsAsync(Guid userId, CancellationToken cancellationToken = default);
        Task AssignOwnerAsync(Guid characterId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Привязывает персонажа к кампании.</summary>
        Task SetCampaignAsync(Guid characterId, Guid campaignId, CancellationToken cancellationToken = default);

        /// <summary>Помечает персонажа как NPC.</summary>
        Task MarkAsNpcAsync(Guid characterId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Выполняет проверки прав доступа на основе контекста текущего пользователя и данных о владении.
    /// </summary>
    public class PermissionChecker(
        IUserSecurityContextProvider contextProvider,
        ICharacterOwnershipRepository characterRepo,
        ILogger<PermissionChecker>? logger = null)
    {
        private readonly IUserSecurityContextProvider _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        private readonly ICharacterOwnershipRepository _characterRepo = characterRepo ?? throw new ArgumentNullException(nameof(characterRepo));
        private readonly ILogger<PermissionChecker> _logger = logger ?? NullLogger<PermissionChecker>.Instance;

        private async Task<UserSecurityContext> GetContextAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return await _contextProvider.GetCurrentContextAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Проверяет, что идентификатор не пустой.
        /// </summary>
        private static void EnsureNotEmpty(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не должен быть пустым: {paramName}", paramName);
        }

        // ---------- Глобальные проверки ----------

        public async Task<bool> IsGameMasterAsync(CancellationToken ct = default)
        {
            var ctx = await GetContextAsync(ct);
            return ctx.GlobalRole is UserRole.GameMaster or UserRole.Admin;
        }

        public async Task<bool> IsAdminAsync(CancellationToken ct = default)
        {
            var ctx = await GetContextAsync(ct);
            return ctx.GlobalRole == UserRole.Admin;
        }

        /// <summary>
        /// Является ли пользователь Мастером указанной кампании (либо глобальным администратором).
        /// </summary>
        public async Task<bool> IsGameMasterOfCampaignAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureNotEmpty(campaignId, nameof(campaignId));
            var ctx = await GetContextAsync(ct);
            if (ctx.GlobalRole == UserRole.Admin) return true;
            return ctx.CampaignRoles.TryGetValue(campaignId, out var role) && role == CampaignRole.GameMaster;
        }

        /// <summary>
        /// Состоит ли пользователь в кампании с любой ролью.
        /// </summary>
        public async Task<bool> IsMemberOfCampaignAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureNotEmpty(campaignId, nameof(campaignId));
            var ctx = await GetContextAsync(ct);
            return ctx.CampaignRoles.ContainsKey(campaignId);
        }

        // ---------- Проверки для персонажей ----------

        /// <summary>
        /// Может ли пользователь просматривать детали персонажа.
        /// Игроки видят только своих персонажей и известных NPC в своих кампаниях.
        /// Мастер/админ видят всех.
        /// </summary>
        public async Task<bool> CanViewCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureNotEmpty(characterId, nameof(characterId));
            var ctx = await GetContextAsync(ct);
            if (ctx.GlobalRole == UserRole.Admin) return true;

            // Владелец персонажа
            if (ctx.OwnedCharacterIds.Contains(characterId))
                return true;

            // Мастер кампании персонажа
            if (ctx.GlobalRole == UserRole.GameMaster)
            {
                var campId = await _characterRepo.GetCampaignIdAsync(characterId, ct).ConfigureAwait(false);
                if (campId.HasValue && await IsGameMasterOfCampaignAsync(campId.Value, ct).ConfigureAwait(false))
                    return true;
            }

            // Игрок может видеть NPC из своей кампании
            if (await _characterRepo.IsNonPlayerCharacterAsync(characterId, ct).ConfigureAwait(false))
            {
                var campId = await _characterRepo.GetCampaignIdAsync(characterId, ct).ConfigureAwait(false);
                if (campId.HasValue && await IsMemberOfCampaignAsync(campId.Value, ct).ConfigureAwait(false))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Может ли пользователь редактировать персонажа.
        /// Игроки могут редактировать только своих персонажей, мастера — любых в своих кампаниях.
        /// </summary>
        public async Task<bool> CanEditCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureNotEmpty(characterId, nameof(characterId));
            var ctx = await GetContextAsync(ct);
            if (ctx.GlobalRole == UserRole.Admin) return true;

            // Владелец-игрок может редактировать своего персонажа
            if (ctx.OwnedCharacterIds.Contains(characterId))
                return true;

            // Мастер кампании может редактировать любого персонажа в ней
            if (ctx.GlobalRole == UserRole.GameMaster)
            {
                var campId = await _characterRepo.GetCampaignIdAsync(characterId, ct).ConfigureAwait(false);
                if (campId.HasValue && await IsGameMasterOfCampaignAsync(campId.Value, ct).ConfigureAwait(false))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Может ли пользователь удалить персонажа. Только администратор или мастер кампании.
        /// </summary>
        public async Task<bool> CanDeleteCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureNotEmpty(characterId, nameof(characterId));
            var ctx = await GetContextAsync(ct);
            if (ctx.GlobalRole == UserRole.Admin) return true;
            if (ctx.GlobalRole != UserRole.GameMaster) return false;

            var campId = await _characterRepo.GetCampaignIdAsync(characterId, ct).ConfigureAwait(false);
            return campId.HasValue && await IsGameMasterOfCampaignAsync(campId.Value, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Может ли пользователь управлять персонажем (атаковать, колдовать, двигаться).
        /// Игрок управляет своими персонажами, мастер — персонажами в своих кампаниях.
        /// </summary>
        public async Task<bool> CanControlCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            EnsureNotEmpty(characterId, nameof(characterId));
            var ctx = await GetContextAsync(ct);
            if (ctx.GlobalRole == UserRole.Admin) return true;

            // Игрок может управлять своим персонажем
            if (ctx.OwnedCharacterIds.Contains(characterId))
                return true;

            // Мастер может управлять персонажами в своей кампании
            if (ctx.GlobalRole == UserRole.GameMaster)
            {
                var campId = await _characterRepo.GetCampaignIdAsync(characterId, ct).ConfigureAwait(false);
                if (campId.HasValue && await IsGameMasterOfCampaignAsync(campId.Value, ct).ConfigureAwait(false))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Может ли пользователь использовать заклинание от имени персонажа. Аналогично управлению.
        /// </summary>
        public Task<bool> CanCastSpellAsync(Guid characterId, CancellationToken ct = default)
            => CanControlCharacterAsync(characterId, ct);

        /// <summary>
        /// Может ли пользователь управлять инвентарём персонажа.
        /// </summary>
        public Task<bool> CanManageInventoryAsync(Guid characterId, CancellationToken ct = default)
            => CanEditCharacterAsync(characterId, ct);

        /// <summary>
        /// Может ли пользователь совершать проверки навыков от имени персонажа.
        /// </summary>
        public Task<bool> CanPerformSkillCheckAsync(Guid characterId, CancellationToken ct = default)
            => CanControlCharacterAsync(characterId, ct);

        // ---------- Проверки для кампании ----------

        public async Task<bool> CanViewCampaignAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureNotEmpty(campaignId, nameof(campaignId));
            var ctx = await GetContextAsync(ct);
            return ctx.GlobalRole == UserRole.Admin || ctx.CampaignRoles.ContainsKey(campaignId);
        }

        public Task<bool> CanEditCampaignAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureNotEmpty(campaignId, nameof(campaignId));
            return IsGameMasterOfCampaignAsync(campaignId, ct);
        }

        public Task<bool> CanStartCombatAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureNotEmpty(campaignId, nameof(campaignId));
            return IsGameMasterOfCampaignAsync(campaignId, ct);
        }

        /// <summary>
        /// Может ли пользователь завершить бой (глобальный мастер или админ).
        /// </summary>
        public Task<bool> CanEndAnyCombatAsync(CancellationToken ct = default)
            => IsGameMasterAsync(ct);

        /// <summary>
        /// Может ли пользователь завершить указанный бой (пока проверка как для любого боя, но может быть расширена).
        /// </summary>
        public Task<bool> CanEndCombatAsync(Guid combatId, CancellationToken ct = default)
        {
            EnsureNotEmpty(combatId, nameof(combatId));
            return IsGameMasterAsync(ct);
        }

        // ---------- NPC ----------

        /// <summary>
        /// Может ли пользователь управлять NPC (глобальный мастер или админ).
        /// </summary>
        public Task<bool> CanManageAnyNpcAsync(CancellationToken ct = default)
            => IsGameMasterAsync(ct);

        /// <summary>
        /// Может ли пользователь управлять конкретным NPC (проверка аналогична общей).
        /// </summary>
        public Task<bool> CanManageNpcAsync(Guid npcId, CancellationToken ct = default)
        {
            EnsureNotEmpty(npcId, nameof(npcId));
            return IsGameMasterAsync(ct);
        }

        // ---------- Другие действия ----------

        public Task<bool> CanSendMessageToCampaignAsync(Guid campaignId, CancellationToken ct = default)
        {
            EnsureNotEmpty(campaignId, nameof(campaignId));
            return IsMemberOfCampaignAsync(campaignId, ct);
        }

        /// <summary>
        /// Любой аутентифицированный пользователь может бросать кости.
        /// </summary>
        public Task<bool> CanRollDiceAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }
}