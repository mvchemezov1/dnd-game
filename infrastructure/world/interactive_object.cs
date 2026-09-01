#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.commands;
using dnd_game.domain.value_objects;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.infrastructure.world
{
    /// <summary>
    /// Тип интерактивного объекта.
    /// </summary>
    public enum InteractiveObjectType
    {
        Door, Chest, Lever, Button, Trap, Altar, Portal,
        Sign, Container, Campfire, Throne, Well, Statue,
        Bookcase, HiddenPassage
    }

    /// <summary>
    /// Состояние объекта.
    /// </summary>
    public enum InteractiveObjectState
    {
        Closed,
        Open,
        Locked,
        Disarmed,
        Armed,
        Activated,
        Deactivated,
        Broken,
        Hidden,
        Revealed
    }

    /// <summary>
    /// Интерактивный объект игрового мира, соответствующий правилам DnD.
    /// Содержит данные для проверок навыков, условий взаимодействия и эффектов.
    /// </summary>
    public sealed class InteractiveObject(ILogger<InteractiveObject>? logger = null)
    {
        private readonly ILogger<InteractiveObject>? _logger = logger;

        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public InteractiveObjectType Type { get; set; }
        public InteractiveObjectState State { get; set; } = InteractiveObjectState.Closed;
        public Position Position { get; set; } = new(0, 0);

        // Условия взаимодействия
        public bool RequiresKey { get; set; }
        public string? RequiredKeyId { get; set; }
        public string? RequiredSpellId { get; set; }
        public string? RequiredQuestFlag { get; set; }
        public int MinimumStrength { get; set; }

        // Проверки навыков и DC
        public int LockpickDC { get; set; }
        public int StrengthDC { get; set; }
        public int DisarmTrapDC { get; set; }
        public int PerceptionDC { get; set; }
        public int InvestigationDC { get; set; }
        public int ArcanaDC { get; set; }

        // Прочность и здоровье
        public int MaxHitPoints { get; set; } = 10;
        public int CurrentHitPoints { get; set; } = 10;
        public int ArmorClass { get; set; } = 15;
        public string DamageImmunities { get; set; } = string.Empty;  // "poison,psychic"
        public string DamageResistances { get; set; } = string.Empty;

        // Последствия взаимодействия
        public int DamageOnFail { get; set; }
        public string DamageTypeOnFail { get; set; } = "piercing";
        public string ConditionOnFail { get; set; } = string.Empty;
        public string? ScriptNameOnOpen { get; set; }
        public string? ScriptNameOnFail { get; set; }
        public string? SoundOnInteract { get; set; }

        // Награды
        public List<string> LootItemIds { get; set; } = [];
        public int Gold { get; set; }
        public int ExperiencePoints { get; set; }
        public int ConditionDurationRounds { get; set; } = 1;

        /// <summary>
        /// Попытка открыть/использовать объект.
        /// </summary>
        public async Task<bool> TryOpenAsync(
            Guid characterId,
            ICommandBus commandBus,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(commandBus);
            cancellationToken.ThrowIfCancellationRequested();

            if (State == InteractiveObjectState.Open || State == InteractiveObjectState.Broken)
                return false;

            // Проверка требований
            if (RequiresKey)
            {
                _logger?.LogWarning("Объект {ObjectId} требует ключ {KeyId}", Id, RequiredKeyId);
                return false; // в реальной системе должна быть проверка наличия ключа в инвентаре
            }

            if (!string.IsNullOrEmpty(RequiredSpellId))
            {
                _logger?.LogWarning("Объект {ObjectId} требует заклинание {SpellId}", Id, RequiredSpellId);
                return false;
            }

            // Если заперто – требуется проверка
            if (State == InteractiveObjectState.Locked)
            {
                _logger?.LogInformation("Объект {ObjectId} заперт; требуется взлом или сила", Id);
                return false;
            }

            // Успех: меняем состояние и выдаём награды
            State = InteractiveObjectState.Open;
            if (!string.IsNullOrEmpty(ScriptNameOnOpen))
            {
                await commandBus.SendAsync(
                    new TriggerScriptCommand(ScriptNameOnOpen,
                        new Dictionary<string, object> { { "ObjectId", Id } }),
                    cancellationToken).ConfigureAwait(false);
            }

            await GrantLootAsync(characterId, commandBus, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Объект {ObjectId} открыт персонажем {CharacterId}", Id, characterId);
            return true;
        }

        /// <summary>
        /// Попытка взлома замка (Ловкость рук или воровские инструменты).
        /// </summary>
        public async Task<string> AttemptPickLockAsync(
            Guid characterId,
            int rollResult,
            int proficiencyBonus,
            int dexterityModifier,
            ICommandBus commandBus,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(commandBus);
            cancellationToken.ThrowIfCancellationRequested();

            if (State != InteractiveObjectState.Locked)
                return "Замок не заперт.";

            int total = rollResult + proficiencyBonus + dexterityModifier;
            if (total >= LockpickDC)
            {
                State = InteractiveObjectState.Closed;
                _logger?.LogInformation("Персонаж {CharacterId} взломал замок объекта {ObjectId}", characterId, Id);
                return "Замок взломан.";
            }

            // Провал: может быть шум, поломка отмычки и т.д.
            if (!string.IsNullOrEmpty(ScriptNameOnFail))
            {
                await commandBus.SendAsync(
                    new TriggerScriptCommand(ScriptNameOnFail,
                        new Dictionary<string, object> { { "CharacterId", characterId } }),
                    cancellationToken).ConfigureAwait(false);
            }

            _logger?.LogWarning("Персонаж {CharacterId} не смог взломать замок объекта {ObjectId}", characterId, Id);
            return "Взлом не удался.";
        }

        /// <summary>
        /// Попытка выбить дверь/поднять силой (Атлетика).
        /// </summary>
        public Task<bool> AttemptForceAsync(
            Guid characterId,
            int athleticsCheck,
            ICommandBus commandBus,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(commandBus);
            cancellationToken.ThrowIfCancellationRequested();

            if (State != InteractiveObjectState.Locked && State != InteractiveObjectState.Closed)
                return Task.FromResult(false);

            bool success = athleticsCheck >= StrengthDC;
            if (success)
            {
                State = InteractiveObjectState.Open;
                _logger?.LogInformation("Персонаж {CharacterId} силой открыл объект {ObjectId}", characterId, Id);
            }
            else
            {
                _logger?.LogWarning("Персонаж {CharacterId} не смог силой открыть объект {ObjectId}", characterId, Id);
            }

            return Task.FromResult(success);
        }

        /// <summary>
        /// Обезвредить ловушку.
        /// </summary>
        public async Task<bool> DisarmTrapAsync(
            Guid characterId,
            int rollResult,
            int proficiencyBonus,
            int dexterityModifier,
            ICommandBus commandBus,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(commandBus);
            cancellationToken.ThrowIfCancellationRequested();

            if (State != InteractiveObjectState.Armed)
                return false;

            int total = rollResult + proficiencyBonus + dexterityModifier;
            if (total >= DisarmTrapDC)
            {
                State = InteractiveObjectState.Disarmed;
                _logger?.LogInformation("Персонаж {CharacterId} обезвредил ловушку объекта {ObjectId}", characterId, Id);
                return true;
            }

            // Активация ловушки
            await ActivateTrapAsync(characterId, commandBus, cancellationToken).ConfigureAwait(false);
            _logger?.LogWarning("Персонаж {CharacterId} активировал ловушку объекта {ObjectId}", characterId, Id);
            return false;
        }

        /// <summary>
        /// Обыскать объект (проверка Внимательности или Расследования).
        /// </summary>
        public async Task<string> SearchAsync(
            Guid characterId,
            int perceptionRoll,
            int investigationRoll,
            ICommandBus commandBus,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(commandBus);
            cancellationToken.ThrowIfCancellationRequested();

            if (State == InteractiveObjectState.Hidden && perceptionRoll >= PerceptionDC)
            {
                State = InteractiveObjectState.Revealed;
                _logger?.LogInformation("Персонаж {CharacterId} заметил скрытый объект {ObjectId}", characterId, Id);
                return "Вы замечаете что-то необычное.";
            }

            if (investigationRoll >= InvestigationDC)
            {
                await GrantLootAsync(characterId, commandBus, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Персонаж {CharacterId} нашёл что-то ценное в объекте {ObjectId}", characterId, Id);
                return "Вы что-то нашли!";
            }

            return "Вы ничего не находите.";
        }

        /// <summary>
        /// Уничтожить объект уроном.
        /// </summary>
        public bool Destroy(int damage, string damageType)
        {
            if (damage <= 0)
                throw new ArgumentOutOfRangeException(nameof(damage), "Урон должен быть положительным.");
            if (string.IsNullOrWhiteSpace(damageType))
                throw new ArgumentException("Тип урона не может быть пустым.", nameof(damageType));

            var immunities = DamageImmunities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resistances = DamageResistances.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (immunities.Any(i => string.Equals(i, damageType, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (resistances.Any(r => string.Equals(r, damageType, StringComparison.OrdinalIgnoreCase)))
                damage /= 2;

            CurrentHitPoints -= damage;
            if (CurrentHitPoints <= 0)
            {
                State = InteractiveObjectState.Broken;
                _logger?.LogInformation("Объект {ObjectId} разрушен", Id);
                return true;
            }
            return false;
        }

        // ---------- Приватные методы ----------

        private async Task ActivateTrapAsync(
            Guid characterId,
            ICommandBus commandBus,
            CancellationToken cancellationToken)
        {
            if (DamageOnFail > 0)
            {
                await commandBus.SendAsync(
                    new DealDamage(characterId, DamageOnFail, DamageTypeOnFail),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(ConditionOnFail))
            {
                await commandBus.SendAsync(
                    new ApplyCondition(characterId, ConditionOnFail, ConditionDurationRounds),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(ScriptNameOnFail))
            {
                await commandBus.SendAsync(
                    new TriggerScriptCommand(ScriptNameOnFail,
                        new Dictionary<string, object> { { "CharacterId", characterId } }),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task GrantLootAsync(
            Guid characterId,
            ICommandBus commandBus,
            CancellationToken cancellationToken)
        {
            foreach (var itemId in LootItemIds)
            {
                await commandBus.SendAsync(
                    new AddInventoryItem(characterId, itemId, itemId),
                    cancellationToken).ConfigureAwait(false);
            }

            if (Gold > 0)
            {
                await commandBus.SendAsync(
                    new AddGold(characterId, Gold),
                    cancellationToken).ConfigureAwait(false);
            }

            if (ExperiencePoints > 0)
            {
                await commandBus.SendAsync(
                    new GainExperience(characterId, ExperiencePoints),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Репозиторий интерактивных объектов.
    /// </summary>
    public interface IInteractiveObjectRepository
    {
        Task<InteractiveObject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<List<InteractiveObject>> GetAllInAreaAsync(int minX, int minY, int maxX, int maxY, CancellationToken cancellationToken = default);
        Task AddAsync(InteractiveObject obj, CancellationToken cancellationToken = default);
        Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
    }
}