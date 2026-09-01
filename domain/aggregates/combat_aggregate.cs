using System;
using System.Collections.Generic;
using System.Linq;
using dnd_game.domain.events;

namespace dnd_game.domain.aggregates
{
    /// <summary>
    /// Агрегат боя. Управляет состоянием боевой сцены: участниками, инициативой, раундами,
    /// ходами, действиями и состояниями.
    /// </summary>
    public class CombatAggregate : AggregateRoot
    {
        // ---------- Состояние боя ----------
        public List<CombatParticipant> Participants { get; private set; } = [];
        public bool IsActive { get; private set; }
        public int Round { get; private set; } = 0;
        public int CurrentTurnIndex { get; private set; } = -1; // -1 пока инициатива не определена
        public List<Guid> PlayerCharacterIds { get; private set; } = [];

        // ---------- Конструкторы ----------
        public CombatAggregate(
            Guid combatId,
            IEnumerable<(Guid CharacterId, int Speed)> participantsWithSpeed,
            IEnumerable<Guid>? playerCharacterIds = null)
        {
            ArgumentNullException.ThrowIfNull(participantsWithSpeed);
            if (combatId == Guid.Empty)
                throw new ArgumentException("Идентификатор боя не может быть пустым.", nameof(combatId));

            var participantIds = participantsWithSpeed.Select(p => p.CharacterId).ToList();
            var speeds = participantsWithSpeed.ToDictionary(p => p.CharacterId, p => p.Speed);
            var playerIds = playerCharacterIds?.ToList() ?? [];

            ApplyChange(new CombatStarted(combatId, participantIds, speeds, playerIds, DateTime.UtcNow));
        }

        // Для event sourcing
        public CombatAggregate() { }

        // ---------- Применение событий ----------
        protected override void ApplyEvent(IDomainEvent @event)
        {
            switch (@event)
            {
                case CombatStarted ev:
                    Id = ev.CombatId;
                    Participants = [.. ev.Participants.Select(id =>
                    {
                        int speed = ev.ParticipantSpeeds.TryGetValue(id, out var spd) ? spd : 30;
                        return new CombatParticipant(id)
                        {
                            Speed = speed,
                            MovementRemaining = speed
                        };
                    })];
                    PlayerCharacterIds = ev.PlayerCharacterIds ?? [];
                    IsActive = true;
                    Round = 0;
                    CurrentTurnIndex = -1;
                    break;

                case CombatEnded:
                    IsActive = false;
                    break;

                case InitiativeRolled ev:
                    var participant = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (participant != null)
                    {
                        participant.Initiative = ev.Initiative;
                        participant.DexterityModifier = ev.DexterityModifier;
                        participant.HasRolledInitiative = true;
                    }
                    break;

                case CombatRoundStarted ev:
                    Round = ev.Round;
                    // Сортировка по инициативе и модификатору ловкости (по убыванию)
                    Participants = [.. Participants
                        .OrderByDescending(p => p.Initiative)
                        .ThenByDescending(p => p.DexterityModifier)];
                    CurrentTurnIndex = 0;
                    // Сбрасываем все флаги действий; они будут выданы в начале хода конкретного участника
                    foreach (var p in Participants)
                    {
                        p.IsCurrentTurn = false;
                        p.HasAction = false;
                        p.HasBonusAction = false;
                        p.HasMovement = false;
                        // Реакция восстанавливается в начале хода, поэтому не трогаем
                    }
                    break;

                case CombatTurnStarted ev:
                    var turnParticipant = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (turnParticipant != null)
                    {
                        turnParticipant.IsCurrentTurn = true;
                        turnParticipant.HasAction = true;
                        turnParticipant.HasBonusAction = true;
                        turnParticipant.HasReaction = true;
                        turnParticipant.HasMovement = true;
                        turnParticipant.MovementRemaining = turnParticipant.Speed;
                    }
                    CurrentTurnIndex = Participants.FindIndex(p => p.CharacterId == ev.CharacterId);
                    break;

                case CombatTurnEnded ev:
                    var endedParticipant = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (endedParticipant != null)
                    {
                        endedParticipant.IsCurrentTurn = false;
                        endedParticipant.HasAction = false;
                        endedParticipant.HasBonusAction = false;
                        endedParticipant.HasMovement = false;
                    }
                    break;

                case CombatRoundEnded:
                    foreach (var p in Participants)
                    {
                        p.IsCurrentTurn = false;
                        p.HasAction = false;
                        p.HasBonusAction = false;
                        p.HasMovement = false;
                    }
                    CurrentTurnIndex = -1;
                    break;

                case ParticipantAddedToCombat ev:
                    if (!Participants.Any(p => p.CharacterId == ev.CharacterId))
                    {
                        Participants.Add(new CombatParticipant(ev.CharacterId)
                        {
                            Initiative = ev.Initiative,
                            HasRolledInitiative = false // новый участник должен бросить инициативу
                        });
                    }
                    break;

                case ParticipantRemovedFromCombat ev:
                    int removedIndex = Participants.FindIndex(p => p.CharacterId == ev.CharacterId);
                    Participants.RemoveAll(p => p.CharacterId == ev.CharacterId);

                    if (Participants.Count == 0)
                    {
                        CurrentTurnIndex = -1;
                        break;
                    }

                    if (removedIndex == CurrentTurnIndex)
                    {
                        // Текущий участник удалён — ход прерывается, следующий начнётся с начала списка
                        CurrentTurnIndex = -1;
                    }
                    else if (removedIndex < CurrentTurnIndex)
                    {
                        // Удалён участник до текущего — сдвигаем индекс
                        CurrentTurnIndex--;
                    }

                    // Гарантируем, что индекс в допустимых пределах
                    if (CurrentTurnIndex >= Participants.Count)
                        CurrentTurnIndex = Participants.Count - 1;
                    break;

                case CombatActionTaken ev:
                    var actor = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (actor != null) actor.HasAction = false;
                    break;

                case CombatBonusActionTaken ev:
                    var bonusActor = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (bonusActor != null) bonusActor.HasBonusAction = false;
                    break;

                case CombatReactionUsed ev:
                    var reactor = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (reactor != null) reactor.HasReaction = false;
                    break;

                case CombatMovementUsed ev:
                    var mover = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (mover != null)
                        mover.MovementRemaining = Math.Max(0, mover.MovementRemaining - ev.Feet);
                    break;

                case ConditionAppliedToCombatant ev:
                    var target = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (target != null && !target.Conditions.Contains(ev.Condition))
                        target.Conditions.Add(ev.Condition);
                    break;

                case ConditionRemovedFromCombatant ev:
                    var condTarget = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    condTarget?.Conditions.Remove(ev.Condition);
                    break;

                case CombatConcentrationStarted ev:
                    var conc = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (conc != null) conc.Concentrating = true;
                    break;

                case CombatConcentrationEnded ev:
                    var concEnd = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (concEnd != null) concEnd.Concentrating = false;
                    break;

                // События, не изменяющие состояние агрегата, но сохраняемые для проекций и аналитики
                case CombatDamageDealt:
                case CombatHealingDealt:
                case CombatSavingThrowMade:
                case CombatDeathSavingThrowMade:
                case CombatParticipantStabilized:
                case CombatConcentrationCheckMade:
                case CombatTurnDelayed:
                case CombatSurrender:
                    break;

                case CombatActionReadied ev:
                    var readiedParticipant = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (readiedParticipant != null)
                    {
                        readiedParticipant.ReadyActionType = ev.ActionType;
                        readiedParticipant.ReadyTriggerCondition = ev.TriggerCondition;
                        readiedParticipant.HasReadiedAction = true;
                        readiedParticipant.HasAction = false; // подготовка тратит обычное действие
                    }
                    break;

                case CombatReadiedActionTriggered ev:
                    var triggeredParticipant = Participants.FirstOrDefault(p => p.CharacterId == ev.CharacterId);
                    if (triggeredParticipant != null)
                    {
                        triggeredParticipant.HasReaction = false; // тратим реакцию при срабатывании
                        triggeredParticipant.HasReadiedAction = false;
                        triggeredParticipant.ReadyActionType = null;
                        triggeredParticipant.ReadyTriggerCondition = null;
                    }
                    break;
            }
        }

        // ---------- Инварианты ----------
        public override void EnsureInvariants()
        {
            if (Round < 0)
                throw new InvalidOperationException("Раунд не может быть отрицательным.");
            if (CurrentTurnIndex < -1 || CurrentTurnIndex >= Participants.Count)
                throw new InvalidOperationException("Индекс текущего хода вне допустимого диапазона.");
            if (Participants.Select(p => p.CharacterId).Distinct().Count() != Participants.Count)
                throw new InvalidOperationException("В бою не может быть дублирующихся участников.");
        }

        // ---------- Команды (методы) ----------

        public void EndCombat()
        {
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            ApplyChange(new CombatEnded(Id, DateTime.UtcNow));
        }

        private static void EnsureCanAct(CombatParticipant participant, bool requireCurrentTurn)
        {
            if (participant == null)
                throw new ArgumentNullException(nameof(participant));

            // Проверяем состояния, запрещающие любые действия
            if (participant.Conditions.Contains("Incapacitated", StringComparer.OrdinalIgnoreCase) ||
                participant.Conditions.Contains("Stunned", StringComparer.OrdinalIgnoreCase) ||
                participant.Conditions.Contains("Paralyzed", StringComparer.OrdinalIgnoreCase) ||
                participant.Conditions.Contains("Unconscious", StringComparer.OrdinalIgnoreCase) ||
                participant.Conditions.Contains("Petrified", StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Участник не может действовать из-за своего состояния.");
            }

            if (requireCurrentTurn && !participant.IsCurrentTurn)
            {
                throw new InvalidOperationException("Сейчас не ход этого участника.");
            }
        }

        public void RollInitiative(Guid characterId, int initiative, int dexterityModifier)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            if (Participants.All(p => p.CharacterId != characterId))
                throw new InvalidOperationException("Персонаж не является участником боя.");
            ApplyChange(new InitiativeRolled(Id, characterId, initiative, dexterityModifier));
        }

        public void StartRound()
        {
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            if (Participants.Count == 0)
                throw new InvalidOperationException("В бою нет участников.");
            if (Participants.Any(p => !p.HasRolledInitiative))
                throw new InvalidOperationException("Не все участники бросили инициативу.");
            ApplyChange(new CombatRoundStarted(Id, Round + 1, DateTime.UtcNow));
        }

        public void StartTurn(Guid characterId)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            var participant = Participants.FirstOrDefault(p => p.CharacterId == characterId)
                ?? throw new InvalidOperationException("Участник не найден.");
            ApplyChange(new CombatTurnStarted(Id, characterId, DateTime.UtcNow));
        }

        public void EndTurn(Guid characterId)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            var participant = Participants.FirstOrDefault(p => p.CharacterId == characterId)
                ?? throw new InvalidOperationException("Участник не найден.");
            if (!participant.IsCurrentTurn)
                throw new InvalidOperationException("Сейчас не ход этого участника.");
            ApplyChange(new CombatTurnEnded(Id, characterId, DateTime.UtcNow));
        }

        public void AddParticipant(Guid characterId, int initiative)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            if (Participants.Any(p => p.CharacterId == characterId))
                throw new InvalidOperationException("Персонаж уже участвует в бою.");
            ApplyChange(new ParticipantAddedToCombat(Id, characterId, initiative));
        }

        public void RemoveParticipant(Guid characterId)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            if (!Participants.Any(p => p.CharacterId == characterId))
                throw new InvalidOperationException("Участник не найден.");
            ApplyChange(new ParticipantRemovedFromCombat(Id, characterId));
        }

        public void UseAction(Guid characterId)
        {
            var p = GetParticipant(characterId);
            EnsureCanAct(p, requireCurrentTurn: true);
            if (!p.HasAction)
                throw new InvalidOperationException("Нет доступного основного действия.");
            ApplyChange(new CombatActionTaken(Id, characterId));
        }

        public void UseBonusAction(Guid characterId)
        {
            var p = GetParticipant(characterId);
            if (!p.IsCurrentTurn)
                throw new InvalidOperationException("Сейчас не ход этого участника.");
            if (!p.HasBonusAction)
                throw new InvalidOperationException("Нет доступного бонусного действия.");
            ApplyChange(new CombatBonusActionTaken(Id, characterId));
        }

        public void UseReaction(Guid characterId)
        {
            var p = GetParticipant(characterId);
            // Реакция может использоваться вне хода, но состояния запрещают
            EnsureCanAct(p, requireCurrentTurn: false);
            if (!p.HasReaction)
                throw new InvalidOperationException("Нет доступной реакции.");
            ApplyChange(new CombatReactionUsed(Id, characterId));
        }

        public void UseMovement(Guid characterId, int feet)
        {
            if (feet <= 0)
                throw new ArgumentOutOfRangeException(nameof(feet), "Дистанция должна быть положительной.");
            var p = GetParticipant(characterId);
            // Перемещение требует, чтобы участник не был ограничен состояниями
            if (p.Conditions.Contains("Restrained", StringComparer.OrdinalIgnoreCase) ||
                p.Conditions.Contains("Grappled", StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Участник обездвижен и не может перемещаться.");
            }
            EnsureCanAct(p, requireCurrentTurn: true);
            if (!p.HasMovement)
                throw new InvalidOperationException("Нет доступного перемещения.");
            if (p.MovementRemaining < feet)
                throw new InvalidOperationException("Недостаточно оставшегося перемещения.");
            ApplyChange(new CombatMovementUsed(Id, characterId, feet));
        }

        public void ApplyCondition(Guid characterId, string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                throw new ArgumentException("Состояние не может быть пустым.", nameof(condition));
            ApplyChange(new ConditionAppliedToCombatant(Id, characterId, condition));
        }

        public void RemoveCondition(Guid characterId, string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                throw new ArgumentException("Состояние не может быть пустым.", nameof(condition));
            var p = GetParticipant(characterId);
            if (!p.Conditions.Contains(condition))
                throw new InvalidOperationException("Состояние не активно.");
            ApplyChange(new ConditionRemovedFromCombatant(Id, characterId, condition));
        }

        public void StartConcentration(Guid characterId)
        {
            var p = GetParticipant(characterId);
            if (p.Concentrating)
                throw new InvalidOperationException("Участник уже концентрируется.");
            ApplyChange(new CombatConcentrationStarted(Id, characterId));
        }

        public void EndConcentration(Guid characterId)
        {
            var p = GetParticipant(characterId);
            if (!p.Concentrating)
                throw new InvalidOperationException("Участник не концентрируется.");
            ApplyChange(new CombatConcentrationEnded(Id, characterId));
        }

        // ---------- Вспомогательные методы для CombatHandler ----------
        public void SetParticipantInitiative(Guid characterId, int initiative, int dexterityModifier)
            => RollInitiative(characterId, initiative, dexterityModifier);

        public void NextTurn()
        {
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            if (Participants.Count == 0)
                return;

            int next = (CurrentTurnIndex + 1) % Participants.Count;
            StartTurn(Participants[next].CharacterId);
        }

        public void EndRound()
        {
            if (!IsActive)
                throw new InvalidOperationException("Бой не активен.");
            if (Round <= 0)
                throw new InvalidOperationException("Раунд ещё не начат.");
            ApplyChange(new CombatRoundEnded(Id, Round, DateTime.UtcNow));
        }

        public void MoveParticipant(Guid participantId, int distanceFeet)
            => UseMovement(participantId, distanceFeet);

        public void PerformStandardAction(Guid participantId, string actionType, Guid? targetId, object? actionData)
        {
            if (string.IsNullOrWhiteSpace(actionType))
                throw new ArgumentException("Тип действия не может быть пустым.", nameof(actionType));

            var p = GetParticipant(participantId);
            EnsureCanAct(p, requireCurrentTurn: true);
            if (!p.HasAction)
                throw new InvalidOperationException("Нет доступного основного действия.");

            // Дополнительные проверки для атаки/заклинания
            if (actionType is "Attack" or "CastSpell" or "Help" && targetId == null)
                throw new ArgumentException("Для этого действия требуется цель.", nameof(targetId));
            if (actionType == "CastSpell" && actionData == null)
                throw new ArgumentException("Для заклинания необходимо указать дополнительные данные.", nameof(actionData));

            ApplyChange(new CombatActionTaken(Id, participantId));
        }

        public void PerformBonusAction(Guid participantId, string actionType, Guid? targetId, object? actionData)
        {
            if (string.IsNullOrWhiteSpace(actionType))
                throw new ArgumentException("Тип бонусного действия не может быть пустым.", nameof(actionType));
            if (actionType == "Attack" && targetId == null)
                throw new ArgumentException("Для бонусной атаки требуется цель.", nameof(targetId));

            var p = GetParticipant(participantId);
            EnsureCanAct(p, requireCurrentTurn: true);
            if (!p.HasBonusAction)
                throw new InvalidOperationException("Нет доступного бонусного действия.");

            ApplyChange(new CombatBonusActionTaken(Id, participantId));
        }

        public void PerformReaction(Guid participantId, string reactionType, string triggerDescription, Guid? targetId)
        {
            if (string.IsNullOrWhiteSpace(reactionType))
                throw new ArgumentException("Тип реакции не может быть пустым.", nameof(reactionType));
            if (string.IsNullOrWhiteSpace(triggerDescription))
                throw new ArgumentException("Описание триггера не может быть пустым.", nameof(triggerDescription));
            if (reactionType == "OpportunityAttack" && targetId == null)
                throw new ArgumentException("Для реакции атаки требуется цель.", nameof(targetId));

            var p = GetParticipant(participantId);
            EnsureCanAct(p, requireCurrentTurn: false);
            if (!p.HasReaction)
                throw new InvalidOperationException("Нет доступной реакции.");

            ApplyChange(new CombatReactionUsed(Id, participantId));
        }

        public void ReadyAction(Guid participantId, string actionToReady, string triggerCondition)
        {
            if (string.IsNullOrWhiteSpace(actionToReady))
                throw new ArgumentException("Действие не может быть пустым.", nameof(actionToReady));
            if (string.IsNullOrWhiteSpace(triggerCondition))
                throw new ArgumentException("Условие срабатывания не может быть пустым.", nameof(triggerCondition));

            var p = GetParticipant(participantId);
            EnsureCanAct(p, requireCurrentTurn: true);
            if (!p.HasAction || !p.HasReaction)
                throw new InvalidOperationException("Нет доступных действия и реакции.");

            ApplyChange(new CombatActionReadied(Id, participantId, actionToReady, triggerCondition));
        }

        public void TriggerReadiedAction(Guid participantId)
        {
            var p = GetParticipant(participantId);
            EnsureCanAct(p, requireCurrentTurn: false);
            if (!p.HasReadiedAction)
                throw new InvalidOperationException("Нет подготовленного действия.");
            if (!p.HasReaction)
                throw new InvalidOperationException("Нет доступной реакции.");

            ApplyChange(new CombatReadiedActionTriggered(Id, participantId, p.ReadyActionType ?? ""));
        }

        public void DealDamage(Guid sourceParticipantId, Guid targetParticipantId, int damageAmount, string damageType)
        {
            if (sourceParticipantId == Guid.Empty || targetParticipantId == Guid.Empty)
                throw new ArgumentException("Идентификатор участника не может быть пустым.");
            if (damageAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(damageAmount), "Урон должен быть положительным.");
            if (string.IsNullOrWhiteSpace(damageType))
                throw new ArgumentException("Тип урона не может быть пустым.", nameof(damageType));
            ApplyChange(new CombatDamageDealt(Id, sourceParticipantId, targetParticipantId, damageAmount, damageType));
        }

        public void HealTarget(Guid sourceParticipantId, Guid targetParticipantId, int healingAmount)
        {
            if (sourceParticipantId == Guid.Empty || targetParticipantId == Guid.Empty)
                throw new ArgumentException("Идентификатор участника не может быть пустым.");
            if (healingAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(healingAmount), "Лечение должно быть положительным.");
            ApplyChange(new CombatHealingDealt(Id, sourceParticipantId, targetParticipantId, healingAmount));
        }

        public void ApplyConditionToParticipant(Guid targetParticipantId, string conditionType, int durationRounds)
        {
            if (durationRounds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationRounds), "Длительность должна быть положительной.");
            ApplyCondition(targetParticipantId, conditionType);
        }

        public void RemoveConditionFromParticipant(Guid targetParticipantId, string conditionType)
            => RemoveCondition(targetParticipantId, conditionType);

        public void MakeSavingThrow(Guid participantId, string ability, int difficultyClass, int rollResult, int modifiers)
        {
            if (string.IsNullOrWhiteSpace(ability))
                throw new ArgumentException("Характеристика не может быть пустой.", nameof(ability));
            if (difficultyClass <= 0)
                throw new ArgumentOutOfRangeException(nameof(difficultyClass), "Сложность должна быть положительной.");
            ApplyChange(new CombatSavingThrowMade(Id, participantId, ability, difficultyClass, rollResult, modifiers));
        }

        public void MakeDeathSavingThrow(Guid participantId, int rollResult)
            => ApplyChange(new CombatDeathSavingThrowMade(Id, participantId, rollResult));

        public void StabilizeParticipant(Guid participantId, Guid stabilizedByParticipantId)
            => ApplyChange(new CombatParticipantStabilized(Id, participantId, stabilizedByParticipantId));

        public void MakeConcentrationCheck(Guid participantId, int difficultyClass, int rollResult, int constitutionModifier)
        {
            if (difficultyClass <= 0)
                throw new ArgumentOutOfRangeException(nameof(difficultyClass), "Сложность должна быть положительной.");
            ApplyChange(new CombatConcentrationCheckMade(Id, participantId, difficultyClass, rollResult, constitutionModifier));
        }

        public void DelayTurn(Guid participantId)
            => ApplyChange(new CombatTurnDelayed(Id, participantId));

        public void Surrender(Guid participantId)
            => ApplyChange(new CombatSurrender(Id, participantId));

        // ---------- Приватные помощники ----------
        private CombatParticipant GetParticipant(Guid characterId)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
            return Participants.FirstOrDefault(p => p.CharacterId == characterId)
                ?? throw new InvalidOperationException("Участник не найден.");
        }
    }

    /// <summary>
    /// Участник боя.
    /// </summary>
    public class CombatParticipant
    {
        public Guid CharacterId { get; }
        public int Initiative { get; set; }
        public int DexterityModifier { get; set; }
        public bool IsCurrentTurn { get; set; }
        public bool HasAction { get; set; }
        public bool HasBonusAction { get; set; }
        public bool HasReaction { get; set; }
        public bool HasMovement { get; set; }
        public int MovementRemaining { get; set; }
        public List<string> Conditions { get; set; } = [];
        public bool Concentrating { get; set; }
        public int Speed { get; set; } = 30;
        public bool HasRolledInitiative { get; set; }
        public string? ReadyActionType { get; set; }
        public string? ReadyTriggerCondition { get; set; }
        public bool HasReadiedAction { get; set; }

        public CombatParticipant(Guid characterId)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
            CharacterId = characterId;
        }
    }
}