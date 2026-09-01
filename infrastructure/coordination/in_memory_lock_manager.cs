#nullable enable
using dnd_game.application.security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.coordination
{
    /// <summary>
    /// Реализация распределённых блокировок в памяти (не для production с несколькими экземплярами).
    /// Подходит для тестов и локальной разработки.
    /// </summary>
    public class InMemoryLockManager(PermissionChecker permissionChecker, ILogger<InMemoryLockManager>? logger = null) : IDistributedLockManager
    {
        private record LockEntry(string LockId, DateTime Expiration);

        private readonly ConcurrentDictionary<string, LockEntry> _locks = new();
        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        private readonly ILogger<InMemoryLockManager> _logger = logger ?? NullLogger<InMemoryLockManager>.Instance;
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

            var deadline = DateTime.UtcNow + timeout;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Удаляем просроченные блокировки
                CleanExpiredLocks();

                var lockId = $"{ownerId}:{Guid.NewGuid():N}";
                var expiration = DateTime.UtcNow + DefaultLockLeaseTime;

                if (_locks.TryAdd(resourceKey, new LockEntry(lockId, expiration)))
                {
                    _logger.LogInformation("Блокировка {ResourceKey} захвачена владельцем {OwnerId} (id={LockId})",
                        resourceKey, ownerId, lockId);
                    return new LockHandle(this, resourceKey, lockId);
                }

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
        public Task ReleaseAsync(string resourceKey, string lockId, CancellationToken cancellationToken = default)
        {
            ValidateResourceKey(resourceKey);
            if (string.IsNullOrWhiteSpace(lockId))
                throw new ArgumentException("Идентификатор блокировки не может быть пустым.", nameof(lockId));
            cancellationToken.ThrowIfCancellationRequested();

            if (_locks.TryGetValue(resourceKey, out var entry) && entry.LockId == lockId)
            {
                _locks.TryRemove(resourceKey, out _);
                _logger.LogInformation("Блокировка {ResourceKey} освобождена (id={LockId})", resourceKey, lockId);
            }
            else
            {
                _logger.LogWarning("Попытка освободить несуществующую или чужую блокировку {ResourceKey} (id={LockId})",
                    resourceKey, lockId);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task ForceReleaseAsync(string resourceKey, Guid masterUserId, CancellationToken cancellationToken = default)
        {
            ValidateResourceKey(resourceKey);
            if (masterUserId == Guid.Empty)
                throw new ArgumentException("Идентификатор мастера не может быть пустым.", nameof(masterUserId));
            cancellationToken.ThrowIfCancellationRequested();

            // Проверяем права через PermissionChecker
            if (!await _permissionChecker.IsGameMasterAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning("Пользователь {UserId} попытался принудительно снять блокировку без прав", masterUserId);
                throw new UnauthorizedAccessException("Только Мастер или администратор может принудительно снять блокировку.");
            }

            _locks.TryRemove(resourceKey, out _);
            _logger.LogWarning("Блокировка {ResourceKey} принудительно снята Мастером {MasterId}", resourceKey, masterUserId);
        }

        /// <inheritdoc />
        public Task<bool> IsLockedAsync(string resourceKey, CancellationToken cancellationToken = default)
        {
            ValidateResourceKey(resourceKey);
            cancellationToken.ThrowIfCancellationRequested();

            if (_locks.TryGetValue(resourceKey, out var entry))
            {
                if (entry.Expiration > DateTime.UtcNow)
                {
                    return Task.FromResult(true);
                }
                // Просроченная блокировка — удаляем
                _locks.TryRemove(resourceKey, out _);
            }
            return Task.FromResult(false);
        }

        private void CleanExpiredLocks()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _locks)
            {
                if (kvp.Value.Expiration <= now)
                {
                    _locks.TryRemove(kvp.Key, out _);
                }
            }
        }

        private static void ValidateResourceKey(string resourceKey)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
                throw new ArgumentException("Ключ ресурса не может быть пустым.", nameof(resourceKey));
        }
    }
}