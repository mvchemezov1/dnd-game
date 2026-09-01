#nullable enable
using System.Threading;

namespace dnd_game.infrastructure.message_bus
{
    /// <summary>
    /// Предоставляет доступ к текущему контексту команды через AsyncLocal.
    /// Устанавливается в шине команд перед выполнением обработчика.
    /// </summary>
    public static class CommandContextAccessor
    {
        private static readonly AsyncLocal<CommandContext?> _current = new();

        public static CommandContext? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        /// <summary>Выполняет действие с заданным контекстом, гарантируя восстановление прежнего значения.</summary>
        public static IDisposable Push(CommandContext? context)
        {
            var previous = _current.Value;
            _current.Value = context;
            return new PopWhenDisposed(previous);
        }

        private sealed class PopWhenDisposed(CommandContext? previous) : IDisposable
        {
            public void Dispose() => _current.Value = previous;
        }
    }
}