#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.commands;
using dnd_game.domain.value_objects;

namespace dnd_game.presentation.client
{
    /// <summary>
    /// Тип шага макроса.
    /// </summary>
    public enum MacroStepType
    {
        /// <summary>Отправить доменную команду.</summary>
        SendCommand,

        /// <summary>Подождать указанное количество миллисекунд.</summary>
        Wait,

        /// <summary>Бросить кости и сохранить результат в переменную.</summary>
        RollDice,

        /// <summary>Если условие истинно, выполнить вложенные шаги.</summary>
        Conditional,

        /// <summary>Повторить вложенные шаги заданное количество раз.</summary>
        Repeat,

        /// <summary>Вывести сообщение в журнал.</summary>
        LogMessage
    }

    /// <summary>
    /// Один шаг макроса.
    /// </summary>
    public sealed class MacroStep
    {
        /// <summary>Тип шага.</summary>
        public MacroStepType Type { get; set; } = MacroStepType.SendCommand;

        /// <summary>Полное имя типа команды (для SendCommand).</summary>
        public string CommandTypeName { get; set; } = string.Empty;

        /// <summary>Параметры команды (для SendCommand).</summary>
        public Dictionary<string, object> CommandParameters { get; set; } = [];

        /// <summary>Время ожидания в миллисекундах (для Wait).</summary>
        public int WaitMilliseconds { get; set; }

        /// <summary>Нотация костей (для RollDice), например "2d6+3".</summary>
        public string DiceNotation { get; set; } = string.Empty;

        /// <summary>Имя переменной, в которую сохраняется результат (для RollDice).</summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>Условное выражение (для Conditional), например "$level >= 5".</summary>
        public string ConditionExpression { get; set; } = string.Empty;

        /// <summary>Вложенные шаги (для Conditional и Repeat).</summary>
        public List<MacroStep> Children { get; set; } = [];

        /// <summary>Количество повторений (для Repeat).</summary>
        public int RepeatCount { get; set; } = 1;

        /// <summary>Сообщение для вывода в журнал (для LogMessage).</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Полное определение макроса.
    /// </summary>
    public sealed class MacroDefinition
    {
        /// <summary>Название макроса.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Описание.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Список шагов.</summary>
        public List<MacroStep> Steps { get; set; } = [];

        /// <summary>Признак системного (встроенного) макроса.</summary>
        public bool IsSystem { get; set; }
    }

    /// <summary>
    /// Контекст выполнения одного экземпляра макроса.
    /// </summary>
    public sealed class MacroExecutionContext
    {
        /// <summary>Идентификатор управляемого персонажа.</summary>
        public Guid ControlledCharacterId { get; set; }

        /// <summary>Идентификатор активного боя, если есть.</summary>
        public Guid? ActiveCombatId { get; set; }

        /// <summary>Переменные макроса.</summary>
        public Dictionary<string, object> Variables { get; set; } = [];

        /// <summary>Токен отмены.</summary>
        public CancellationToken CancellationToken { get; set; }
    }

    /// <summary>
    /// Репозиторий макросов.
    /// </summary>
    public interface IMacroRepository
    {
        /// <summary>Возвращает макрос по имени.</summary>
        Task<MacroDefinition?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>Возвращает все макросы.</summary>
        Task<List<MacroDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>Сохраняет макрос.</summary>
        Task SaveAsync(MacroDefinition macro, CancellationToken cancellationToken = default);

        /// <summary>Удаляет макрос по имени.</summary>
        Task DeleteAsync(string name, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Система макросов, позволяющая игрокам автоматизировать последовательности действий.
    /// Поддерживает встроенные макросы и пользовательские из репозитория.
    /// </summary>
    public sealed class MacroEngine
    {
        private readonly IMacroRepository _repository;
        private readonly IGameClient _client;
        private readonly ILogger<MacroEngine> _logger;
        private readonly ConcurrentDictionary<string, MacroDefinition> _builtinMacros;

        public MacroEngine(
            IMacroRepository repository,
            IGameClient client,
            ILogger<MacroEngine> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _builtinMacros = new ConcurrentDictionary<string, MacroDefinition>(StringComparer.OrdinalIgnoreCase);
            RegisterBuiltinMacros();
        }

        /// <summary>
        /// Выполняет макрос по имени с заданным контекстом.
        /// </summary>
        public async Task ExecuteMacroAsync(string macroName, MacroExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(macroName))
                throw new ArgumentException("Имя макроса не может быть пустым.", nameof(macroName));
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            var macro = await _repository.GetByNameAsync(macroName, context.CancellationToken).ConfigureAwait(false)
                        ?? _builtinMacros.GetValueOrDefault(macroName);

            if (macro == null)
            {
                _logger.LogWarning("Макрос '{MacroName}' не найден.", macroName);
                return;
            }

            // Предзаполняем стандартные переменные
            context.Variables["characterId"] = context.ControlledCharacterId;

            // Если targetId ещё не задан, устанавливаем пустой Guid
            if (!context.Variables.ContainsKey("targetId"))
                context.Variables["targetId"] = Guid.Empty;

            await ExecuteStepsAsync(macro.Steps, context).ConfigureAwait(false);
        }

        /// <summary>
        /// Выполняет список шагов макроса.
        /// </summary>
        private async Task ExecuteStepsAsync(List<MacroStep> steps, MacroExecutionContext context)
        {
            if (steps == null || steps.Count == 0)
                return;

            foreach (var step in steps)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    switch (step.Type)
                    {
                        case MacroStepType.SendCommand:
                            await ExecuteSendCommandAsync(step, context).ConfigureAwait(false);
                            break;
                        case MacroStepType.Wait:
                            await Task.Delay(step.WaitMilliseconds, context.CancellationToken).ConfigureAwait(false);
                            break;
                        case MacroStepType.RollDice:
                            ExecuteRollDice(step, context);
                            break;
                        case MacroStepType.Conditional:
                            if (EvaluateCondition(step.ConditionExpression, context))
                                await ExecuteStepsAsync(step.Children, context).ConfigureAwait(false);
                            break;
                        case MacroStepType.Repeat:
                            for (int i = 0; i < step.RepeatCount; i++)
                                await ExecuteStepsAsync(step.Children, context).ConfigureAwait(false);
                            break;
                        case MacroStepType.LogMessage:
                            _logger.LogInformation("Сообщение макроса: {Message}", step.Message);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Выполнение макроса отменено.");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка выполнения шага макроса типа {StepType}", step.Type);
                }
            }
        }

        /// <summary>
        /// Создаёт и отправляет команду из шага SendCommand.
        /// </summary>
        private async Task ExecuteSendCommandAsync(MacroStep step, MacroExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(step.CommandTypeName))
            {
                _logger.LogWarning("Не указан тип команды для шага SendCommand.");
                return;
            }

            var commandType = Type.GetType(step.CommandTypeName);
            if (commandType == null)
            {
                _logger.LogWarning("Неизвестный тип команды: {CommandType}", step.CommandTypeName);
                return;
            }

            // Рекурсивно подставляем переменные и вычисляем выражения
            var resolvedParams = new Dictionary<string, object?>();
            foreach (var kvp in step.CommandParameters)
            {
                resolvedParams[kvp.Key] = ResolveValue(kvp.Value, context);
            }

            ICommand? command;
            try
            {
                var json = JsonSerializer.Serialize(resolvedParams, commandType);
                command = JsonSerializer.Deserialize(json, commandType) as ICommand;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось создать команду {CommandType} для макроса", step.CommandTypeName);
                return;
            }

            if (command != null)
                await _client.SendCommandAsync(command).ConfigureAwait(false);
        }

        /// <summary>
        /// Рекурсивно разрешает значения переменных и выражений.
        /// </summary>
        private object ResolveValue(object? value, MacroExecutionContext context)
        {
            switch (value)
            {
                case string str when str.StartsWith('$'):
                    // Переменная вида "$variable" или выражение "$a + 10"
                    var varName = str[1..];
                    if (context.Variables.TryGetValue(varName, out var varValue))
                        return varValue;

                    if (str.Contains(' ') || str.Contains('+') || str.Contains('-') ||
                        str.Contains('*') || str.Contains('/'))
                    {
                        return EvaluateExpression(str, context);
                    }

                    _logger.LogWarning("Переменная '{VarName}' не найдена в контексте макроса", varName);
                    return str;

                case string str:
                    // Проверяем, не является ли строка арифметическим выражением
                    if (str.Contains('+') || str.Contains('-') || str.Contains('*') || str.Contains('/'))
                        return EvaluateExpression(str, context);
                    return str;

                case Dictionary<string, object?> dict:
                    var newDict = new Dictionary<string, object?>();
                    foreach (var kv in dict)
                        newDict[kv.Key] = ResolveValue(kv.Value, context);
                    return newDict;

                case List<object?> list:
                    return list.Select(item => ResolveValue(item, context)).ToList();

                default:
                    return value ?? string.Empty;
            }
        }

        /// <summary>
        /// Вычисляет простое арифметическое выражение вида "операнд оператор операнд".
        /// Поддерживаются целые числа и переменные.
        /// </summary>
        private object EvaluateExpression(string expression, MacroExecutionContext context)
        {
            var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                throw new FormatException($"Некорректное выражение: {expression}");

            var left = ResolveOperand(parts[0], context);
            var right = ResolveOperand(parts[2], context);
            var op = parts[1];

            if (left is int l && right is int r)
            {
                return op switch
                {
                    "+" => l + r,
                    "-" => l - r,
                    "*" => l * r,
                    "/" => l / r,
                    _ => throw new FormatException($"Неподдерживаемый оператор: {op}")
                };
            }

            return $"{left} {op} {right}";
        }

        /// <summary>
        /// Разрешает операнд: переменная или число.
        /// </summary>
        private object ResolveOperand(string operand, MacroExecutionContext context)
        {
            if (operand.StartsWith('$'))
            {
                var varName = operand[1..];
                if (context.Variables.TryGetValue(varName, out var value))
                    return value;
                _logger.LogWarning("Переменная '{VarName}' не найдена в контексте макроса", varName);
                return operand;
            }

            if (int.TryParse(operand, out int intValue))
                return intValue;

            return operand;
        }

        /// <summary>
        /// Выполняет бросок костей и сохраняет результат в переменную.
        /// </summary>
        private void ExecuteRollDice(MacroStep step, MacroExecutionContext context)
        {
            try
            {
                var dice = Dice.Parse(step.DiceNotation);
                var result = dice.Roll(Random.Shared);
                context.Variables[step.VariableName] = result.Total;
                context.Variables[$"{step.VariableName}_details"] = result.KeptRolls.ToList();
                _logger.LogDebug("Бросок макроса {Notation} = {Total}", step.DiceNotation, result.Total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Некорректная нотация костей в макросе: {Notation}", step.DiceNotation);
            }
        }

        /// <summary>
        /// Вычисляет условие для шага Conditional. Поддерживаются сравнения чисел и строк.
        /// </summary>
        private static bool EvaluateCondition(string expression, MacroExecutionContext context)
        {
            var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return false;

            var varName = parts[0];
            var op = parts[1];
            var rightStr = parts[2];

            if (!context.Variables.TryGetValue(varName, out var leftValue))
                return false;

            // Пробуем интерпретировать как числа
            if (TryConvertToDouble(leftValue, out double leftNum) &&
                TryConvertToDouble(rightStr, out double rightNum))
            {
                return op switch
                {
                    "==" => leftNum == rightNum,
                    "!=" => leftNum != rightNum,
                    ">" => leftNum > rightNum,
                    "<" => leftNum < rightNum,
                    ">=" => leftNum >= rightNum,
                    "<=" => leftNum <= rightNum,
                    _ => false
                };
            }

            // Если не числа — сравниваем как строки
            string leftStr = leftValue?.ToString() ?? string.Empty;
            return op switch
            {
                "==" => string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static bool TryConvertToDouble(object? value, out double result)
        {
            switch (value)
            {
                case double d:
                    result = d;
                    return true;
                case int i:
                    result = i;
                    return true;
                case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        // --------------------------------------------------------------------------------
        // Встроенные макросы D&D
        // --------------------------------------------------------------------------------

        private void RegisterBuiltinMacros()
        {
            // FullAttack – стандартная атака по текущей цели
            _builtinMacros["fullattack"] = new MacroDefinition
            {
                Name = "fullattack",
                Description = "Выполняет стандартную атаку по текущей цели.",
                IsSystem = true,
                Steps =
                [
                    new MacroStep
                    {
                        Type = MacroStepType.SendCommand,
                        CommandTypeName = "dnd_game.domain.commands.TakeStandardAction",
                        CommandParameters = new Dictionary<string, object>
                        {
                            { "ParticipantId", "$characterId" },
                            { "ActionType", "Attack" },
                            { "TargetId", "$targetId" }
                        }
                    }
                ]
            };

            // SecondWind – Второе дыхание (1d10 + уровень бойца)
            _builtinMacros["secondwind"] = new MacroDefinition
            {
                Name = "secondwind",
                Description = "Использует «Второе дыхание»: восстановление 1d10 + уровень бойца.",
                IsSystem = true,
                Steps =
                [
                    new MacroStep { Type = MacroStepType.RollDice, DiceNotation = "1d10", VariableName = "secondwind_roll" },
                    new MacroStep
                    {
                        Type = MacroStepType.SendCommand,
                        CommandTypeName = "dnd_game.domain.commands.HealCharacter",
                        CommandParameters = new Dictionary<string, object>
                        {
                            { "CharacterId", "$characterId" },
                            { "Amount", "$secondwind_roll" }
                        }
                    }
                ]
            };

            // Fireball – Огненный шар: 8d6 урона огнём
            _builtinMacros["fireball"] = new MacroDefinition
            {
                Name = "fireball",
                Description = "Сотворяет Огненный шар: 8d6 урона огнём.",
                IsSystem = true,
                Steps =
                [
                    new MacroStep { Type = MacroStepType.RollDice, DiceNotation = "8d6", VariableName = "fireball_damage" },
                    new MacroStep
                    {
                        Type = MacroStepType.LogMessage,
                        Message = "Огненный шар взрывается!"
                    },
                    new MacroStep
                    {
                        Type = MacroStepType.SendCommand,
                        CommandTypeName = "dnd_game.domain.commands.TakeStandardAction",
                        CommandParameters = new Dictionary<string, object>
                        {
                            { "ParticipantId", "$characterId" },
                            { "ActionType", "CastSpell" },
                            { "TargetId", "$targetId" },
                            { "ActionData", "$fireball_damage" }
                        }
                    }
                ]
            };
        }

        /// <summary>
        /// Возвращает список встроенных макросов.
        /// </summary>
        public IReadOnlyList<MacroDefinition> GetBuiltinMacros()
        {
            return [.. _builtinMacros.Values];
        }
    }
}