#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.message_bus
{
    /// <summary>
    /// Хранит ключи идемпотентности обработанных команд.
    /// </summary>
    public interface IIdempotencyStore
    {
        /// <summary>Пытается добавить ключ. Возвращает false, если ключ уже существует.</summary>
        Task<bool> TryAddAsync(Guid key, TimeSpan lifetime, CancellationToken cancellationToken = default);

        /// <summary>Проверяет, существует ли ключ.</summary>
        Task<bool> ContainsAsync(Guid key, CancellationToken cancellationToken = default);
    }
}