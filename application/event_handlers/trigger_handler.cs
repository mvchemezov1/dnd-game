using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.application.event_handlers
{
    /// <summary>
    /// Описывает одно действие внутри скрипта.
    /// </summary>
    public class ScriptAction
    {
        public string ActionType { get; set; } = string.Empty; // "SpawnMonster", "GiveItem", "Teleport", "SetQuestFlag" и т.д.
        public Dictionary<string, object> Parameters { get; set; } = [];
    }

    /// <summary>
    /// Условие, которое проверяется перед запуском скрипта.
    /// </summary>
    public class TriggerCondition
    {
        public string ConditionType { get; set; } = string.Empty; // "SkillCheck", "HasItem", "IsAlive", "LevelGreaterThan" и т.д.
        public Dictionary<string, object> Parameters { get; set; } = [];
    }

    /// <summary>
    /// Хранилище определений триггеров, загружаемых из базы данных Мастера.
    /// </summary>
    public interface ITriggerDefinitionRepository
    {
        Task<IEnumerable<TriggerDefinition>> GetByEventAsync(
            string eventName,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Определение триггера.
    /// </summary>
    public class TriggerDefinition
    {
        public Guid TriggerId { get; set; }
        public string EventName { get; set; } = string.Empty;   // имя доменного события, на которое реагируем
        public List<TriggerCondition> Conditions { get; set; } = [];
        public List<ScriptAction> Actions { get; set; } = [];
        public bool IsOneShot { get; set; } = true;             // сработать только один раз
        public int CooldownSeconds { get; set; } = 0;           // перезарядка в секундах (0 – без перезарядки)
        public int DelaySeconds { get; set; } = 0;              // задержка перед выполнением действий
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// Состояние конкретного триггера (активен, на перезарядке, уже использован).
    /// </summary>
    public class TriggerState
    {
        public bool HasBeenTriggered { get; set; }
        public DateTime? LastTriggeredUtc { get; set; }
        public DateTime? CooldownEndsUtc { get; set; }
    }

    /// <summary>
    /// Интерфейс для проверки условий (использует read-модель).
    /// </summary>
    public interface IConditionEvaluator
    {
        Task<bool> EvaluateAsync(TriggerCondition condition, IDomainEvent triggeringEvent, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Интерфейс для хранения и получения состояния триггеров (для тестируемости и персистентности).
    /// </summary>
    public interface ITriggerStateStore
    {
        Task<TriggerState?> GetAsync(Guid triggerId, CancellationToken cancellationToken);
        Task SaveAsync(Guid triggerId, TriggerState state, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Реализация хранилища состояний триггеров в памяти.
    /// </summary>
    public class InMemoryTriggerStateStore : ITriggerStateStore
    {
        private readonly ConcurrentDictionary<Guid, TriggerState> _states = [];

        public Task<TriggerState?> GetAsync(Guid triggerId, CancellationToken cancellationToken)
        {
            _states.TryGetValue(triggerId, out var state);
            return Task.FromResult(state);
        }

        public Task SaveAsync(Guid triggerId, TriggerState state, CancellationToken cancellationToken)
        {
            _states[triggerId] = state;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Обработчик скриптовых триггеров. Реагирует на доменные события,
    /// проверяет условия и выполняет предписанные действия через командную шину.
    /// </summary>
    public class TriggerHandler(
        ITriggerDefinitionRepository definitionRepo,
        IConditionEvaluator conditionEvaluator,
        ICommandBus commandBus,
        ITriggerStateStore stateStore,
        ILogger<TriggerHandler> logger) : IEventHandler<IDomainEvent>, IDisposable
    {
        private readonly ITriggerDefinitionRepository _definitionRepo = definitionRepo ?? throw new ArgumentNullException(nameof(definitionRepo));
        private readonly IConditionEvaluator _conditionEvaluator = conditionEvaluator ?? throw new ArgumentNullException(nameof(conditionEvaluator));
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly ITriggerStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        private readonly ILogger<TriggerHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Task> _delayedTasks = [];
        private readonly object _lock = new();

        public async Task Handle(IDomainEvent @event, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(@event);
            cancellationToken.ThrowIfCancellationRequested();

            string eventName = @event.GetType().Name;

            // Получаем все определения триггеров, которые реагируют на данный тип события
            var definitions = await _definitionRepo.GetByEventAsync(eventName, cancellationToken);
            if (definitions == null || !definitions.Any())
                return;

            foreach (var definition in definitions.OrderBy(d => d.Priority))
            {
                // Проверяем состояние триггера
                var state = await _stateStore.GetAsync(definition.TriggerId, cancellationToken)
                            ?? new TriggerState();

                // Если одноразовый и уже срабатывал – пропускаем
                if (definition.IsOneShot && state.HasBeenTriggered)
                    continue;

                // Если на перезарядке – пропускаем
                if (state.CooldownEndsUtc.HasValue && state.CooldownEndsUtc.Value > DateTime.UtcNow)
                    continue;

                // Проверяем все условия
                bool conditionsMet = true;
                foreach (var condition in definition.Conditions)
                {
                    if (!await _conditionEvaluator.EvaluateAsync(condition, @event, cancellationToken))
                    {
                        conditionsMet = false;
                        break;
                    }
                }
                if (!conditionsMet)
                    continue;

                // Условия выполнены: обновляем состояние
                state.HasBeenTriggered = true;
                state.LastTriggeredUtc = DateTime.UtcNow;
                if (definition.CooldownSeconds > 0)
                {
                    state.CooldownEndsUtc = DateTime.UtcNow.AddSeconds(definition.CooldownSeconds);
                }
                await _stateStore.SaveAsync(definition.TriggerId, state, cancellationToken);

                _logger.LogInformation("Триггер {TriggerId} активирован событием {EventName}", definition.TriggerId, eventName);

                // Применяем задержку, если задана
                if (definition.DelaySeconds > 0)
                {
                    ScheduleDelayedExecution(definition, cancellationToken);
                }
                else
                {
                    await ExecuteActionsAsync(definition, cancellationToken);
                }
            }
        }

        private void ScheduleDelayedExecution(TriggerDefinition definition, CancellationToken cancellationToken)
        {
            // Создаём связанный токен, чтобы при отмене внешнего токена или при Dispose задачи отменялись
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            var task = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(definition.DelaySeconds), linkedCts.Token);
                    await ExecuteActionsAsync(definition, linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Отложенный триггер {TriggerId} был отменён", definition.TriggerId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при выполнении отложенного триггера {TriggerId}", definition.TriggerId);
                }
            }, linkedCts.Token);

            // Сохраняем задачу для контроля (например, для ожидания при Dispose)
            lock (_lock)
            {
                _delayedTasks.Add(task);
                // Удаляем завершённые задачи, чтобы не копить
                _delayedTasks.RemoveAll(t => t.IsCompleted);
            }
        }

        private async Task ExecuteActionsAsync(TriggerDefinition definition, CancellationToken cancellationToken)
        {
            foreach (var action in definition.Actions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var command = BuildCommand(action);
                    if (command != null)
                    {
                        await _commandBus.SendAsync(command);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Не удалось выполнить действие {ActionType} триггера {TriggerId}",
                        action.ActionType, definition.TriggerId);
                }
            }
        }

        /// <summary>
        /// Создаёт команду на основе типа действия и его параметров.
        /// Возвращает null, если тип неизвестен или параметры некорректны.
        /// </summary>
        private ICommand? BuildCommand(ScriptAction action)
        {
            try
            {
                switch (action.ActionType)
                {
                    case "SpawnMonster":
                        return new SpawnMonsterCommand(
                            GetStringParam(action, "TemplateId"),
                            GetIntParam(action, "X"),
                            GetIntParam(action, "Y"));

                    case "GiveItem":
                        return new GiveItemCommand(
                            GetGuidParam(action, "CharacterId"),
                            GetStringParam(action, "ItemId"),
                            GetStringParam(action, "ItemName", GetStringParam(action, "ItemId")),
                            GetIntParam(action, "Quantity", 1));

                    case "Teleport":
                        return new TeleportCommand(
                            GetGuidParam(action, "CharacterId"),
                            GetIntParam(action, "DestinationX"),
                            GetIntParam(action, "DestinationY"));

                    case "SetQuestFlag":
                        return new SetQuestFlagCommand(
                            GetGuidParam(action, "CharacterId"),
                            GetStringParam(action, "QuestId"),
                            GetStringParam(action, "Flag"),
                            GetStringParam(action, "Value"));

                    case "StartDialog":
                        return new StartScriptedDialogueCommand(
                            GetGuidParam(action, "InitiatorId"),
                            GetStringParam(action, "DialogId"));

                    case "PlaySound":
                        return new PlaySoundCommand(
                            GetStringParam(action, "SoundName"),
                            GetIntParam(action, "PositionX"),
                            GetIntParam(action, "PositionY"));

                    default:
                        _logger.LogWarning("Неизвестный тип действия триггера: {ActionType}", action.ActionType);
                        return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при построении команды действия {ActionType}", action.ActionType);
                return null;
            }
        }

        private static string GetStringParam(ScriptAction action, string key, string defaultValue = "")
            => action.Parameters.TryGetValue(key, out var value) ? value?.ToString() ?? defaultValue : defaultValue;

        private static int GetIntParam(ScriptAction action, string key, int defaultValue = 0)
            => action.Parameters.TryGetValue(key, out var value) && value is int intVal ? intVal : defaultValue;

        private static Guid GetGuidParam(ScriptAction action, string key)
            => action.Parameters.TryGetValue(key, out var value) && value is Guid guidVal ? guidVal : Guid.Empty;

        /// <summary>
        /// Сбрасывает состояние триггера (может использоваться для отладки или через консоль Мастера).
        /// </summary>
        public async Task ResetTriggerAsync(Guid triggerId, CancellationToken cancellationToken)
        {
            await _stateStore.SaveAsync(triggerId, new TriggerState(), cancellationToken);
        }

        public void Dispose()
        {
            _cts.Cancel();
            lock (_lock)
            {
                Task.WaitAll([.. _delayedTasks], TimeSpan.FromSeconds(5));
                _delayedTasks.Clear();
            }
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}