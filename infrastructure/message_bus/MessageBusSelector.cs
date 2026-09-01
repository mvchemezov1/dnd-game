#nullable enable
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace dnd_game.infrastructure.message_bus
{
    /// <summary>
    /// Определяет, какую реализацию шины команд/событий использовать в данный момент.
    /// По умолчанию используется InMemoryBus. После успешной инициализации RabbitMQ
    /// переключается на RabbitMqBus.
    /// </summary>
    public sealed class MessageBusSelector
    {
        private readonly ILogger<MessageBusSelector> _logger;
        private ICommandBus _commandBus;
        private IEventBus _eventBus;
        private bool _isRabbitMqActive;

        public MessageBusSelector(ICommandBus initialCommandBus, IEventBus initialEventBus, ILogger<MessageBusSelector>? logger = null)
        {
            _commandBus = initialCommandBus ?? throw new ArgumentNullException(nameof(initialCommandBus));
            _eventBus = initialEventBus ?? throw new ArgumentNullException(nameof(initialEventBus));
            _logger = logger ?? NullLogger<MessageBusSelector>.Instance;
        }

        public ICommandBus CommandBus => _commandBus;
        public IEventBus EventBus => _eventBus;
        public bool IsRabbitMqActive => _isRabbitMqActive;

        /// <summary>
        /// Переключает шины на RabbitMQ.
        /// </summary>
        public void UseRabbitMq(RabbitMqBus rabbitMqBus)
        {
            if (rabbitMqBus == null) throw new ArgumentNullException(nameof(rabbitMqBus));
            _commandBus = rabbitMqBus;
            _eventBus = rabbitMqBus;
            _isRabbitMqActive = true;
            _logger.LogInformation("Шина сообщений переключена на RabbitMQ.");
        }
    }
}