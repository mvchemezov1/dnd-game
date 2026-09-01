using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;

namespace dnd_game.application.command_handlers
{
    /// <summary>
    /// Базовый класс для обработчиков команд, работающих с агрегатом Combat.
    /// Содержит общую логику загрузки и сохранения агрегата.
    /// </summary>
    public abstract class CombatCommandHandlerBase(IEventStore eventStore)
    {
        protected readonly IEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));

        /// <summary>
        /// Загружает агрегат Combat по идентификатору. Если агрегат не найден, выбрасывает исключение с русским сообщением.
        /// </summary>
        protected async Task<CombatAggregate> GetCombatAsync(Guid combatId, CancellationToken cancellationToken)
        {
            var aggregate = await _eventStore.Load<CombatAggregate>(combatId, cancellationToken) ?? throw new InvalidAction("Бой не найден");
            return aggregate;
        }

        /// <summary>
        /// Сохраняет изменения агрегата в Event Store.
        /// </summary>
        protected async Task SaveCombatAsync(CombatAggregate aggregate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(aggregate);
            await _eventStore.Save(aggregate, cancellationToken);
        }
    }

    /// <summary>
    /// Обработчик команд для управления боевыми сценами.
    /// Реализует все команды, связанные с инициативой, раундами, действиями и эффектами в бою.
    /// </summary>
    public class CombatHandler(IEventStore eventStore) : CombatCommandHandlerBase(eventStore),
                                 ICommandHandler<StartCombat>,
                                 ICommandHandler<EndCombat>,
                                 ICommandHandler<RollInitiative>,
                                 ICommandHandler<StartRound>,
                                 ICommandHandler<NextTurn>,
                                 ICommandHandler<EndRound>,
                                 ICommandHandler<AddParticipantToCombat>,
                                 ICommandHandler<RemoveParticipantFromCombat>,
                                 ICommandHandler<TakeMoveAction>,
                                 ICommandHandler<TakeStandardAction>,
                                 ICommandHandler<TakeBonusAction>,
                                 ICommandHandler<TakeReaction>,
                                 ICommandHandler<ReadyAction>,
                                 ICommandHandler<TriggerReadyAction>,
                                 ICommandHandler<DealDamageToTarget>,
                                 ICommandHandler<HealTarget>,
                                 ICommandHandler<ApplyConditionToTarget>,
                                 ICommandHandler<RemoveConditionFromTarget>,
                                 ICommandHandler<MakeSavingThrowInCombat>,
                                 ICommandHandler<MakeDeathSavingThrowInCombat>,
                                 ICommandHandler<StabilizeInCombat>,
                                 ICommandHandler<MakeConcentrationCheck>,
                                 ICommandHandler<DelayTurn>,
                                 ICommandHandler<SurrenderInCombat>,
                                 ICommandHandler<PerformAction>,
                                 ICommandHandler<HelpAction>,
                                 ICommandHandler<HideAction>,
                                 ICommandHandler<SearchAction>,
                                 ICommandHandler<UseObjectAction>
    {
        public async Task Handle(StartCombat command, CancellationToken cancellationToken)
        {
            var participantsWithSpeed = new List<(Guid CharacterId, int Speed)>();
            foreach (var participantId in command.Participants)
            {
                int speed = 30; // Скорость по умолчанию
                if (command.ParticipantSpeeds != null && command.ParticipantSpeeds.TryGetValue(participantId, out var providedSpeed))
                {
                    speed = providedSpeed;
                }
                else
                {
                    // Если скорость не предоставлена, загружаем агрегат персонажа для получения значения
                    var character = await _eventStore.Load<CharacterAggregate>(participantId, cancellationToken);
                    if (character != null)
                        speed = character.Speed;
                }
                participantsWithSpeed.Add((participantId, speed));
            }

            var aggregate = new CombatAggregate(command.CombatId, participantsWithSpeed, command.PlayerCharacterIds);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(EndCombat command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.EndCombat();
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(RollInitiative command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.SetParticipantInitiative(command.ParticipantId, command.InitiativeRoll, command.DexterityModifier);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(StartRound command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.StartRound();
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(NextTurn command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.NextTurn();
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(EndRound command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.EndRound();
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(AddParticipantToCombat command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.AddParticipant(command.ParticipantId, command.Initiative);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(RemoveParticipantFromCombat command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.RemoveParticipant(command.ParticipantId);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(TakeMoveAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.MoveParticipant(command.ParticipantId, command.DistanceFeet);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(TakeStandardAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.PerformStandardAction(command.ParticipantId, command.ActionType, command.TargetId, command.ActionData);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(TakeBonusAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.PerformBonusAction(command.ParticipantId, command.ActionType, command.TargetId, command.ActionData);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(TakeReaction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.PerformReaction(command.ParticipantId, command.ReactionType, command.TriggerDescription, command.TargetId);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(ReadyAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.ReadyAction(command.ParticipantId, command.ActionToReady, command.TriggerCondition);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(TriggerReadyAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.TriggerReadiedAction(command.ParticipantId);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(DealDamageToTarget command, CancellationToken cancellationToken)
        {
            // 1. Загружаем бой и создаём событие CombatDamageDealt
            var combat = await GetCombatAsync(command.CombatId, cancellationToken);
            combat.DealDamage(command.SourceParticipantId, command.TargetParticipantId, command.DamageAmount, command.DamageType);
            await SaveCombatAsync(combat, cancellationToken);

            // 2. Применяем урон к персонажу-цели
            var targetCharacter = await _eventStore.Load<CharacterAggregate>(command.TargetParticipantId, cancellationToken)
                ?? throw new InvalidAction("Целевой персонаж не найден.");
            targetCharacter.TakeDamage(command.DamageAmount, command.DamageType);
            await _eventStore.Save(targetCharacter, cancellationToken);
        }

        public async Task Handle(HealTarget command, CancellationToken cancellationToken)
        {
            // 1. Загружаем бой и создаём событие CombatHealingDealt
            var combat = await GetCombatAsync(command.CombatId, cancellationToken);
            combat.HealTarget(command.SourceParticipantId, command.TargetParticipantId, command.HealingAmount);
            await SaveCombatAsync(combat, cancellationToken);

            // 2. Применяем лечение к персонажу-цели
            var targetCharacter = await _eventStore.Load<CharacterAggregate>(command.TargetParticipantId, cancellationToken)
                ?? throw new InvalidAction("Целевой персонаж не найден.");
            targetCharacter.Heal(command.HealingAmount);
            await _eventStore.Save(targetCharacter, cancellationToken);
        }

        public async Task Handle(ApplyConditionToTarget command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.ApplyConditionToParticipant(command.TargetParticipantId, command.ConditionType, command.DurationRounds);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(RemoveConditionFromTarget command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.RemoveConditionFromParticipant(command.TargetParticipantId, command.ConditionType);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(MakeSavingThrowInCombat command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.MakeSavingThrow(command.ParticipantId, command.Ability, command.DifficultyClass, command.RollResult, command.Modifiers);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(MakeDeathSavingThrowInCombat command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.MakeDeathSavingThrow(command.ParticipantId, command.RollResult);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(StabilizeInCombat command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.StabilizeParticipant(command.ParticipantId, command.StabilizedByParticipantId);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(MakeConcentrationCheck command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.MakeConcentrationCheck(command.ParticipantId, command.DifficultyClass, command.RollResult, command.ConstitutionModifier);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(DelayTurn command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.DelayTurn(command.ParticipantId);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(SurrenderInCombat command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            aggregate.Surrender(command.ParticipantId);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(PerformAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);

            // Диспетчеризация по типу действия
            switch (command.ActionType.ToLowerInvariant())
            {
                case "attack":
                case "standardattack":
                    aggregate.PerformStandardAction(command.ParticipantId, "Attack", command.TargetId, command.ActionData);
                    break;

                case "castspell":
                    aggregate.PerformStandardAction(command.ParticipantId, "CastSpell", command.TargetId, command.ActionData);
                    break;

                case "dash":
                    aggregate.PerformStandardAction(command.ParticipantId, "Dash", null, null);
                    break;

                case "disengage":
                    aggregate.PerformStandardAction(command.ParticipantId, "Disengage", null, null);
                    break;

                case "dodge":
                    aggregate.PerformStandardAction(command.ParticipantId, "Dodge", null, null);
                    break;

                case "help":
                    aggregate.PerformStandardAction(command.ParticipantId, "Help", command.TargetId, null);
                    break;

                case "hide":
                    aggregate.PerformStandardAction(command.ParticipantId, "Hide", null, null);
                    break;

                case "ready":
                    aggregate.ReadyAction(command.ParticipantId, "Ready", command.ActionData?.ToString() ?? "");
                    break;

                case "useobject":
                    aggregate.PerformStandardAction(command.ParticipantId, "UseObject", command.TargetId, command.ActionData);
                    break;

                case "bonus":
                case "bonusaction":
                    aggregate.PerformBonusAction(command.ParticipantId, command.ActionType, command.TargetId, command.ActionData);
                    break;

                case "reaction":
                    aggregate.PerformReaction(command.ParticipantId, command.ActionType, command.ActionData?.ToString() ?? "", command.TargetId);
                    break;

                case "move":
                    {
                        if (command.ActionData is null)
                            throw new InvalidAction("Для действия перемещения необходимо указать дистанцию в футах.");

                        int distance;
                        if (command.ActionData is int intDistance)
                            distance = intDistance;
                        else if (command.ActionData is string stringDistance && int.TryParse(stringDistance, out distance))
                        {
                            // преобразование успешно
                        }
                        else
                            throw new InvalidAction($"Некорректные данные для перемещения. Ожидалось число, получено {command.ActionData.GetType().Name}.");

                        if (distance <= 0)
                            throw new InvalidAction("Дистанция должна быть положительным числом.");

                        aggregate.MoveParticipant(command.ParticipantId, distance);
                        break;
                    }

                default:
                    throw new InvalidAction($"Неизвестный тип действия: {command.ActionType}");
            }

            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(HelpAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            // Помощь цели — стандартное действие
            aggregate.PerformStandardAction(command.HelperId, "Help", command.TargetId, null);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(HideAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            // Скрытие — стандартное действие
            aggregate.PerformStandardAction(command.HiderId, "Hide", null, null);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(SearchAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            // Поиск — стандартное действие
            aggregate.PerformStandardAction(command.SearcherId, "Search", null, null);
            await SaveCombatAsync(aggregate, cancellationToken);
        }

        public async Task Handle(UseObjectAction command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCombatAsync(command.CombatId, cancellationToken);
            // Использование объекта — стандартное действие
            aggregate.PerformStandardAction(command.UserId, "UseObject", command.ObjectId, null);
            await SaveCombatAsync(aggregate, cancellationToken);
        }
    }
}