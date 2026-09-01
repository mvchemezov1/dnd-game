#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.commands;

namespace dnd_game.infrastructure.message_bus
{
    /// <summary>
    /// Контекст выполнения команды, содержащий информацию о пользователе, игровой сессии и токен отмены.
    /// </summary>
    public class CommandContext
    {
        /// <summary>Идентификатор пользователя, отправившего команду.</summary>
        public Guid UserId { get; set; }

        /// <summary>Идентификатор игровой сессии (кампании), в которой выполняется команда.</summary>
        public Guid GameSessionId { get; set; }

        /// <summary>Токен отмены для асинхронной операции.</summary>
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    }

    /// <summary>
    /// Универсальная шина команд игры DnD.
    /// Отвечает за маршрутизацию команд к соответствующим обработчикам.
    /// Реализации: InMemoryBus и RabbitMqBus (см. in_memory_bus.cs / rabbitmq_bus.cs).
    /// </summary>
    public interface ICommandBus
    {
        /// <summary>
        /// Отправляет команду в шину для обработки.
        /// </summary>
        /// <param name="command">Команда (не может быть null).</param>
        /// <param name="context">Дополнительный контекст выполнения (может быть null).</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        Task SendAsync(ICommand command, CommandContext? context = null);

        /// <summary>
        /// Подписывает обработчик на команды указанного типа.
        /// </summary>
        /// <typeparam name="TCommand">Тип команды, реализующей <see cref="ICommand"/>.</typeparam>
        /// <param name="handler">Обработчик команды (принимает команду и контекст, возвращает Task).</param>
        void Subscribe<TCommand>(Func<TCommand, CommandContext?, Task> handler) where TCommand : ICommand;
    }

    /// <summary>
    /// Поведение конвейера обработки команд (middleware).
    /// Позволяет добавить сквозную функциональность: логирование, авторизацию, валидацию,
    /// обработку ошибок и т.п. вокруг вызова обработчика команды.
    /// </summary>
    /// <remarks>
    /// В текущих реализациях (InMemoryBus, RabbitMqBus) конвейер пока не подключён.
    /// Интерфейс подготовлен для будущего использования.
    /// </remarks>
    public interface ICommandPipelineBehavior
    {
        /// <summary>
        /// Обрабатывает команду в контексте конвейера.
        /// </summary>
        /// <typeparam name="TCommand">Тип команды.</typeparam>
        /// <param name="command">Команда.</param>
        /// <param name="context">Контекст выполнения.</param>
        /// <param name="next">Делегат, вызывающий следующий шаг конвейера или обработчик.</param>
        /// <returns>Задача.</returns>
        Task HandleAsync<TCommand>(
            TCommand command,
            CommandContext context,
            Func<Task> next) where TCommand : ICommand;
    }

    /// <summary>
    /// Методы расширения для удобной работы с <see cref="ICommandBus"/>.
    /// </summary>
    public static class CommandBusExtensions
    {
        /// <summary>
        /// Отправляет команду с явным токеном отмены.
        /// </summary>
        public static Task SendAsync(
            this ICommandBus commandBus,
            ICommand command,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(commandBus);
            ArgumentNullException.ThrowIfNull(command);

            var context = new CommandContext
            {
                CancellationToken = cancellationToken
            };
            return commandBus.SendAsync(command, context);
        }

        /// <summary>
        /// Отправляет команду с указанием пользователя и токена отмены.
        /// </summary>
        public static Task SendAsync(
            this ICommandBus commandBus,
            ICommand command,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(commandBus);
            ArgumentNullException.ThrowIfNull(command);

            var context = new CommandContext
            {
                UserId = userId,
                CancellationToken = cancellationToken
            };
            return commandBus.SendAsync(command, context);
        }

        /// <summary>
        /// Отправляет команду с полным контекстом (пользователь, сессия, токен).
        /// </summary>
        public static Task SendAsync(
            this ICommandBus commandBus,
            ICommand command,
            Guid userId,
            Guid gameSessionId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(commandBus);
            ArgumentNullException.ThrowIfNull(command);

            var context = new CommandContext
            {
                UserId = userId,
                GameSessionId = gameSessionId,
                CancellationToken = cancellationToken
            };
            return commandBus.SendAsync(command, context);
        }
    }
}