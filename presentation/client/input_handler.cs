#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.commands;

namespace dnd_game.presentation.client
{
    /// <summary>
    /// Режим ввода, определяющий доступные команды.
    /// </summary>
    public enum InputMode
    {
        Normal,          // вне боя: разговор, исследование, отдых
        Combat,          // боевой режим: ограниченный набор действий
        TargetSelection, // выбор цели для заклинания/атаки
        Dialogue,        // диалог с NPC (выбор вариантов ответа)
        Inventory,       // управление инвентарём
        Spellbook,       // просмотр и подготовка заклинаний
        Crafting         // крафт
    }

    /// <summary>
    /// Результат обработки ввода.
    /// </summary>
    public sealed class InputResult
    {
        public bool Success { get; set; }
        public ICommand? Command { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Привязка клавиш (key bindings) для быстрых действий.
    /// </summary>
    public sealed class KeyBindings
    {
        public Dictionary<ConsoleKey, string> Bindings { get; set; } = new()
        {
            { ConsoleKey.A, "attack" },
            { ConsoleKey.M, "move" },
            { ConsoleKey.D, "dash" },
            { ConsoleKey.G, "disengage" },
            { ConsoleKey.H, "hide" },
            { ConsoleKey.I, "inventory" },
            { ConsoleKey.S, "spells" },
            { ConsoleKey.C, "character" },
            { ConsoleKey.R, "rest" },
            { ConsoleKey.E, "end_turn" },
            { ConsoleKey.Enter, "confirm" },
            { ConsoleKey.Escape, "cancel" }
        };
    }

    /// <summary>
    /// Обработчик ввода, преобразующий текстовые команды и нажатия клавиш в доменные команды DnD.
    /// </summary>
    public sealed class InputHandler(
        IGameClient client,
        ILogger<InputHandler> logger,
        KeyBindings? keyBindings = null)
    {
        private readonly IGameClient _client = client ?? throw new ArgumentNullException(nameof(client));
        private readonly KeyBindings _keyBindings = keyBindings ?? new KeyBindings();
        private readonly ILogger<InputHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private Func<Guid, Task<InputResult>>? _pendingTargetCommand;

        /// <summary>Текущий режим ввода.</summary>
        public InputMode CurrentMode { get; set; } = InputMode.Normal;

        /// <summary>Идентификатор персонажа, которым управляет игрок.</summary>
        public Guid ControlledCharacterId { get; set; }

        /// <summary>Идентификатор активного боя, если есть.</summary>
        public Guid? ActiveCombatId { get; set; }

        /// <summary>Идентификатор активного диалога, если есть.</summary>
        public Guid? ActiveDialogueId { get; set; }

        // Словарь псевдонимов команд
        private static readonly Dictionary<string, string> CommandAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "a", "attack" },
            { "m", "move" },
            { "atk", "attack" },
            { "mv", "move" },
            { "dsh", "dash" },
            { "dng", "disengage" },
            { "hde", "hide" },
            { "inv", "inventory" },
            { "spl", "spells" },
            { "chr", "character" },
            { "rst", "rest" },
            { "end", "end_turn" },
            { "loot", "take_all" },
            { "eq", "equip" },
            { "uneq", "unequip" },
            { "use", "use_item" },
            { "drop", "drop_item" },
            { "talk", "speak" }
        };

        /// <summary>
        /// Обрабатывает текстовый ввод (чат-команда или консоль).
        /// </summary>
        public async Task<InputResult> ProcessInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Error("Пустой ввод.");

            // В зависимости от режима обрабатываем ввод особым образом
            if (CurrentMode == InputMode.Dialogue)
                return await ProcessDialogueInput(input).ConfigureAwait(false);

            if (CurrentMode == InputMode.TargetSelection)
                return await ProcessTargetSelection(input).ConfigureAwait(false);

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return Error("Пустой ввод.");

            string commandName = ResolveAlias(parts[0]);
            var args = parts.Length > 1 ? parts[1..] : [];

            return commandName.ToLowerInvariant() switch
            {
                "attack" => await HandleAttack(args).ConfigureAwait(false),
                "move" => await HandleMove(args).ConfigureAwait(false),
                "dash" => await SendCommandAsync(new MoveCharacterWithDash(ControlledCharacterId)).ConfigureAwait(false),
                "disengage" => await SendCommandAsync(new MoveCharacterWithDisengage(ControlledCharacterId)).ConfigureAwait(false),
                "hide" => await SendCommandAsync(new MoveCharacterStealthily(ControlledCharacterId)).ConfigureAwait(false),
                "cast" => await HandleCastSpell(args).ConfigureAwait(false),
                "heal" => await HandleHeal(args).ConfigureAwait(false),
                "rest" => await HandleRest(args).ConfigureAwait(false),
                "use_item" => await HandleUseItem(args).ConfigureAwait(false),
                "equip" => await HandleEquip(args).ConfigureAwait(false),
                "unequip" => await HandleUnequip(args).ConfigureAwait(false),
                "take_all" => await SendCommandAsync(new LootAll(ControlledCharacterId)).ConfigureAwait(false),
                "drop_item" => await HandleDropItem(args).ConfigureAwait(false),
                "speak" => await HandleSpeak(args).ConfigureAwait(false),
                "character" => Ok("Запрошен просмотр персонажа."),
                "inventory" => Ok("Запрос на отображение инвентаря."),
                "spells" => Ok("Запрос на отображение книги заклинаний."),
                "end_turn" => await HandleEndTurn().ConfigureAwait(false),
                "cancel" => Ok("Действие отменено."),
                "help" => Ok(GetHelpText()),
                _ => Error($"Неизвестная команда: '{commandName}'. Введите 'help' для списка доступных команд.")
            };
        }

        /// <summary>
        /// Обрабатывает нажатие клавиши (быстрое действие).
        /// </summary>
        public async Task<InputResult> ProcessKey(ConsoleKey key)
        {
            if (_keyBindings.Bindings.TryGetValue(key, out var command))
            {
                return await ProcessInput(command).ConfigureAwait(false);
            }
            return Error($"Нет привязки для клавиши {key}.");
        }

        // --------------------------------------------------------------------------------
        // Обработчики конкретных команд
        // --------------------------------------------------------------------------------

        private async Task<InputResult> HandleAttack(string[] args)
        {
            if (CurrentMode != InputMode.Combat)
                return Error("Атаковать можно только в бою.");

            // Пытаемся распарсить идентификатор цели, если он указан
            Guid targetId = args.Length > 0 && Guid.TryParse(args[0], out var tid) ? tid : Guid.Empty;
            string actionType = args.Length > 1 && args[1].Equals("ranged", StringComparison.OrdinalIgnoreCase)
                ? "RangedAttack" : "Attack";

            if (targetId == Guid.Empty)
            {
                // Сохраняем ожидающую команду атаки и переходим в режим выбора цели
                _pendingTargetCommand = async selectedTargetId =>
                {
                    var cmd = new TakeStandardAction(ActiveCombatId ?? Guid.Empty, ControlledCharacterId, actionType, selectedTargetId);
                    return await SendCommandAsync(cmd).ConfigureAwait(false);
                };
                CurrentMode = InputMode.TargetSelection;
                return Error("Выберите цель (кликните или введите ID цели).");
            }

            var command = new TakeStandardAction(ActiveCombatId ?? Guid.Empty, ControlledCharacterId, actionType, targetId);
            return await SendCommandAsync(command).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleMove(string[] args)
        {
            // move <x> <y> или move <direction> <distance>
            if (args.Length >= 2 && int.TryParse(args[0], out int x) && int.TryParse(args[1], out int y))
            {
                var cmd = new MoveCharacter(ControlledCharacterId, x, y);
                return await SendCommandAsync(cmd).ConfigureAwait(false);
            }

            // Поддержка направлений (north, south, east, west, ne, nw, se, sw) и расстояния
            if (args.Length >= 1)
            {
                int distance = args.Length > 1 && int.TryParse(args[1], out var d) && d > 0 ? d : 5;
                (int dx, int dy) = args[0].ToLower() switch
                {
                    "north" or "n" => (0, distance),
                    "south" or "s" => (0, -distance),
                    "east" or "e" => (distance, 0),
                    "west" or "w" => (-distance, 0),
                    "ne" or "northeast" => (distance, distance),
                    "nw" or "northwest" => (-distance, distance),
                    "se" or "southeast" => (distance, -distance),
                    "sw" or "southwest" => (-distance, -distance),
                    _ => (int.MinValue, int.MinValue) // индикатор ошибки
                };

                if (dx == int.MinValue && dy == int.MinValue)
                    return Error("Неизвестное направление. Используйте: north, south, east, west, ne, nw, se, sw.");

                var cmd = new MoveCharacter(ControlledCharacterId, dx, dy);
                return await SendCommandAsync(cmd).ConfigureAwait(false);
            }

            return Error("Использование: move <x> <y> или move <direction> <distance>");
        }

        private async Task<InputResult> HandleCastSpell(string[] args)
        {
            if (args.Length < 1)
                return Error("Использование: cast <spell_id> [target] [slot_level]");

            string spellId = args[0];

            int slotLevel = 1;
            if (args.Length > 2)
            {
                if (!int.TryParse(args[2], out slotLevel) || slotLevel < 1 || slotLevel > 9)
                    return Error("Некорректный уровень ячейки. Используйте число от 1 до 9.");
            }

            Guid? targetId = null;
            if (args.Length > 1 && Guid.TryParse(args[1], out var tId))
                targetId = tId;

            if (targetId == null && CurrentMode == InputMode.Combat)
            {
                _pendingTargetCommand = async selectedTargetId =>
                {
                    var cmd = new CastSpell(ControlledCharacterId, spellId, selectedTargetId, slotLevel);
                    return await SendCommandAsync(cmd).ConfigureAwait(false);
                };
                CurrentMode = InputMode.TargetSelection;
                return Error($"Выберите цель для заклинания {spellId} (ячейка {slotLevel}).");
            }

            var command = new CastSpell(ControlledCharacterId, spellId, targetId, slotLevel);
            return await SendCommandAsync(command).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleHeal(string[] args)
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int amount) || amount <= 0)
                return Error("Использование: heal <amount> (положительное число)");

            return await SendCommandAsync(new HealCharacter(ControlledCharacterId, amount)).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleRest(string[] args)
        {
            string restType = args.Length > 0 && args[0].Equals("long", StringComparison.OrdinalIgnoreCase)
                ? "Long" : "Short";
            return await SendCommandAsync(new StartRest(ControlledCharacterId, restType)).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleUseItem(string[] args)
        {
            if (args.Length < 1)
                return Error("Использование: use_item <item_id>");
            return await SendCommandAsync(new UseItem(ControlledCharacterId, args[0])).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleEquip(string[] args)
        {
            if (args.Length < 2)
                return Error("Использование: equip <item_id> <slot>");
            // Здесь имя предмета условно совпадает с ID; в реальной системе нужно получать имя из справочника.
            return await SendCommandAsync(new EquipItem(ControlledCharacterId, args[0], args[1], args[0])).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleUnequip(string[] args)
        {
            if (args.Length < 1)
                return Error("Использование: unequip <item_id>");
            return await SendCommandAsync(new UnequipItem(ControlledCharacterId, args[0])).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleDropItem(string[] args)
        {
            if (args.Length < 1)
                return Error("Использование: drop <item_id> [quantity]");
            int qty = args.Length > 1 && int.TryParse(args[1], out var q) && q > 0 ? q : 1;
            return await SendCommandAsync(new RemoveInventoryItem(ControlledCharacterId, args[0], qty)).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleSpeak(string[] args)
        {
            if (args.Length < 1)
                return Error("Использование: speak <message>");
            return await SendCommandAsync(new SpeakCommand(ControlledCharacterId, string.Join(" ", args))).ConfigureAwait(false);
        }

        private async Task<InputResult> HandleEndTurn()
        {
            if (ActiveCombatId == null)
                return Error("Вы не в бою.");
            return await SendCommandAsync(new NextTurn(ActiveCombatId.Value)).ConfigureAwait(false);
        }

        // --------------------------------------------------------------------------------
        // Специальные режимы ввода
        // --------------------------------------------------------------------------------

        private async Task<InputResult> ProcessDialogueInput(string input)
        {
            if (!ActiveDialogueId.HasValue)
            {
                CurrentMode = InputMode.Normal;
                return Error("Диалог не активен.");
            }

            // Ввод номера варианта ответа
            if (int.TryParse(input, out int optionIndex))
            {
                return await SendCommandAsync(new SelectDialogueOption(ActiveDialogueId.Value, optionIndex)).ConfigureAwait(false);
            }

            // Свободный текст (если NPC принимает текстовые ответы)
            return await SendCommandAsync(new DialogueTextInput(ActiveDialogueId.Value, input)).ConfigureAwait(false);
        }

        private async Task<InputResult> ProcessTargetSelection(string input)
        {
            // Отмена выбора цели
            if (input.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                _pendingTargetCommand = null;
                CurrentMode = InputMode.Combat;
                return Ok("Выбор цели отменён.");
            }

            if (Guid.TryParse(input, out var targetId))
            {
                if (_pendingTargetCommand != null)
                {
                    var pending = _pendingTargetCommand;
                    _pendingTargetCommand = null;
                    CurrentMode = InputMode.Combat;
                    return await pending(targetId).ConfigureAwait(false);
                }

                // Если нет ожидающей команды – это ошибка логики, но обрабатываем мягко.
                CurrentMode = InputMode.Combat;
                return Error("Нет ожидающей команды. Повторите действие.");
            }

            return Error("Некорректная цель. Введите ID цели или 'cancel' для отмены.");
        }

        // --------------------------------------------------------------------------------
        // Вспомогательные методы
        // --------------------------------------------------------------------------------

        private static string ResolveAlias(string input)
            => CommandAliases.TryGetValue(input, out var resolved) ? resolved : input;

        private async Task<InputResult> SendCommandAsync(ICommand command)
        {
            try
            {
                await _client.SendCommandAsync(command).ConfigureAwait(false);
                return new InputResult { Success = true, Command = command, Message = "Команда отправлена." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось отправить команду {CommandType}", command.GetType().Name);
                return Error($"Ошибка: {ex.Message}");
            }
        }

        private static InputResult Ok(string message) => new() { Success = true, Message = message };
        private static InputResult Error(string message) => new() { Success = false, Message = message };

        private string GetHelpText()
        {
            return CurrentMode switch
            {
                InputMode.Combat => "Боевые команды: attack, move, dash, disengage, hide, cast, use_item, end_turn",
                InputMode.Normal => "Команды: move, rest, inventory, spells, equip, unequip, use, drop, speak, help",
                _ => "Доступные команды: help, cancel"
            };
        }
    }
}