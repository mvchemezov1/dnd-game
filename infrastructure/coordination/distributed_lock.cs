#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using dnd_game.application.security;

namespace dnd_game.infrastructure.coordination
{
    /// <summary>
    /// Тип ресурса для блокировки в мире DnD.
    /// </summary>
    public enum LockResourceType
    {
        /// <summary>Персонаж: Character:{id}</summary>
        Character,

        /// <summary>Бой: Combat:{id}</summary>
        Combat,

        /// <summary>Кампания: Campaign:{id}</summary>
        Campaign,

        /// <summary>Инвентарь: Inventory:{characterId}</summary>
        Inventory,

        /// <summary>Торговое предложение: Trade:{offerId}</summary>
        Trade,

        /// <summary>Глобальная блокировка (например, смена времени суток).</summary>
        Global
    }

    /// <summary>
    /// Режим блокировки.
    /// </summary>
    public enum LockMode
    {
        /// <summary>Полная (эксклюзивная) блокировка — только один владелец.</summary>
        Exclusive,

        /// <summary>Разделяемая блокировка — несколько читателей могут удерживать одновременно.</summary>
        Shared
    }

    /// <summary>
    /// Дескриптор захваченной блокировки. Освобождает ресурс при завершении.
    /// </summary>
    public sealed class LockHandle : IAsyncDisposable
    {
        private readonly IDistributedLockManager _manager;
        private readonly string _resourceKey;
        private readonly string _lockId;
        private bool _disposed;

        public string ResourceKey => _resourceKey;
        public string LockId => _lockId;
        public DateTime AcquiredAt { get; }

        internal LockHandle(IDistributedLockManager manager, string resourceKey, string lockId)
        {
            _manager = manager;
            _resourceKey = resourceKey;
            _lockId = lockId;
            AcquiredAt = DateTime.UtcNow;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await _manager.ReleaseAsync(_resourceKey, _lockId).ConfigureAwait(false);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Менеджер распределённых блокировок.
    /// </summary>
    public interface IDistributedLockManager
    {
        /// <summary>
        /// Попытаться захватить блокировку ресурса. Возвращает <c>null</c>, если не удалось.
        /// </summary>
        /// <param name="resourceKey">Ключ ресурса (например, "Character:1234").</param>
        /// <param name="mode">Режим блокировки.</param>
        /// <param name="ownerId">Идентификатор владельца (userId, sessionId и т.п.).</param>
        /// <param name="timeout">Максимальное время ожидания захвата.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Дескриптор блокировки или <c>null</c>, если не удалось захватить за отведённое время.</returns>
        Task<LockHandle?> AcquireAsync(
            string resourceKey,
            LockMode mode,
            string ownerId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Освобождает блокировку.
        /// </summary>
        /// <param name="resourceKey">Ключ ресурса.</param>
        /// <param name="lockId">Идентификатор блокировки, выданный при захвате.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task ReleaseAsync(string resourceKey, string lockId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Принудительно снимает блокировку (только Мастер или администратор).
        /// </summary>
        /// <param name="resourceKey">Ключ ресурса.</param>
        /// <param name="masterUserId">Идентификатор пользователя, запросившего снятие.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task ForceReleaseAsync(string resourceKey, Guid masterUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет, удерживается ли блокировка в данный момент.
        /// </summary>
        /// <param name="resourceKey">Ключ ресурса.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<bool> IsLockedAsync(string resourceKey, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Реализация распределённых блокировок на основе Redis.
    /// </summary>
    /// <remarks>
    /// Используется команда <c>SET lock:key value NX PX leaseTime</c>.
    /// Все блокировки считаются эксклюзивными: параметр <see cref="LockMode"/> пока не влияет на поведение.
    /// В будущем можно реализовать разделяемые блокировки через счётчики.
    /// </remarks>
    public class RedisDistributedLockManager(
        IConnectionMultiplexer redis,
        PermissionChecker permissionChecker,
        ILogger<RedisDistributedLockManager> logger) : IDistributedLockManager
    {
        private readonly IConnectionMultiplexer _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        private readonly IDatabase _db = redis.GetDatabase();
        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        private readonly ILogger<RedisDistributedLockManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>Время аренды блокировки по умолчанию. По истечении блокировка автоматически снимается.</summary>
        private static readonly TimeSpan DefaultLockLeaseTime = TimeSpan.FromSeconds(30);

        /// <inheritdoc />
        public async Task<LockHandle?> AcquireAsync(
            string resourceKey,
            LockMode mode,
            string ownerId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ValidateResourceKey(resourceKey);
            if (string.IsNullOrWhiteSpace(ownerId))
                throw new ArgumentException("Идентификатор владельца не может быть пустым.", nameof(ownerId));
            if (timeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Время ожидания не может быть отрицательным.");
            cancellationToken.ThrowIfCancellationRequested();

            string lockId = $"{ownerId}:{Guid.NewGuid():N}";
            string lockKey = BuildLockKey(resourceKey);
            var deadline = DateTime.UtcNow + timeout;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool acquired = await _db.LockTakeAsync(lockKey, lockId, DefaultLockLeaseTime).ConfigureAwait(false);
                if (acquired)
                {
                    _logger.LogInformation("Блокировка {ResourceKey} захвачена владельцем {OwnerId} (id={LockId})",
                        resourceKey, ownerId, lockId);
                    return new LockHandle(this, resourceKey, lockId);
                }

                // Ждём 100 мс перед следующей попыткой, если ещё есть время
                if (DateTime.UtcNow + TimeSpan.FromMilliseconds(100) < deadline)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    break;
                }
            }
            while (DateTime.UtcNow < deadline);

            _logger.LogDebug("Не удалось захватить блокировку {ResourceKey} для {OwnerId} за {Timeout}",
                resourceKey, ownerId, timeout);
            return null;
        }

        /// <inheritdoc />
        public async Task ReleaseAsync(string resourceKey, string lockId, CancellationToken cancellationToken = default)
        {
            ValidateResourceKey(resourceKey);
            if (string.IsNullOrWhiteSpace(lockId))
                throw new ArgumentException("Идентификатор блокировки не может быть пустым.", nameof(lockId));
            cancellationToken.ThrowIfCancellationRequested();

            string lockKey = BuildLockKey(resourceKey);
            await _db.LockReleaseAsync(lockKey, lockId).ConfigureAwait(false);
            _logger.LogInformation("Блокировка {ResourceKey} освобождена (id={LockId})", resourceKey, lockId);
        }

        /// <inheritdoc />
        public async Task ForceReleaseAsync(string resourceKey, Guid masterUserId, CancellationToken cancellationToken = default)
        {
            ValidateResourceKey(resourceKey);
            if (masterUserId == Guid.Empty)
                throw new ArgumentException("Идентификатор мастера не может быть пустым.", nameof(masterUserId));
            cancellationToken.ThrowIfCancellationRequested();

            // Проверяем, что вызывающий действительно Мастер или администратор
            if (!await _permissionChecker.IsGameMasterAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning("Пользователь {UserId} попытался принудительно снять блокировку без прав", masterUserId);
                throw new UnauthorizedAccessException("Только Мастер или администратор может принудительно снять блокировку.");
            }

            string lockKey = BuildLockKey(resourceKey);
            await _db.KeyDeleteAsync(lockKey).ConfigureAwait(false);
            _logger.LogWarning("Блокировка {ResourceKey} принудительно снята Мастером {MasterId}", resourceKey, masterUserId);
        }

        /// <inheritdoc />
        public async Task<bool> IsLockedAsync(string resourceKey, CancellationToken cancellationToken = default)
        {
            ValidateResourceKey(resourceKey);
            cancellationToken.ThrowIfCancellationRequested();

            string lockKey = BuildLockKey(resourceKey);
            return await _db.KeyExistsAsync(lockKey).ConfigureAwait(false);
        }

        private static string BuildLockKey(string resourceKey) => $"lock:{resourceKey}";

        private static void ValidateResourceKey(string resourceKey)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
                throw new ArgumentException("Ключ ресурса не может быть пустым.", nameof(resourceKey));
        }
    }

    /// <summary>
    /// Фабрика для создания ключей блокировок.
    /// </summary>
    public static class LockKeyFactory
    {
        public static string ForCharacter(Guid characterId) => $"{LockResourceType.Character}:{characterId}";
        public static string ForCombat(Guid combatId) => $"{LockResourceType.Combat}:{combatId}";
        public static string ForCampaign(Guid campaignId) => $"{LockResourceType.Campaign}:{campaignId}";
        public static string ForInventory(Guid characterId) => $"{LockResourceType.Inventory}:{characterId}";
        public static string ForTrade(Guid offerId) => $"{LockResourceType.Trade}:{offerId}";
        public static string ForGlobal(string name) => $"{LockResourceType.Global}:{name}";
        public static string ForSaga(Guid sagaId) => $"Saga:{sagaId}";
    }
}