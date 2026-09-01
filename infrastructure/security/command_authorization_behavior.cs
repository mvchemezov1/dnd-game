#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.infrastructure.security
{
    public class CommandAuthorizationBehavior : ICommandPipelineBehavior
    {
        private readonly PolicyEnforcer _policyEnforcer;
        private readonly PermissionChecker _permissionChecker;

        public CommandAuthorizationBehavior(PolicyEnforcer policyEnforcer, PermissionChecker permissionChecker)
        {
            _policyEnforcer = policyEnforcer;
            _permissionChecker = permissionChecker;
        }

        public async Task HandleAsync<TCommand>(
            TCommand command,
            CommandContext context,
            Func<Task> next) where TCommand : ICommand
        {
            if (command is null) throw new ArgumentNullException(nameof(command));

            if (!RequiresAuthorization(command))
            {
                await next();
                return;
            }

            switch (command)
            {
                // ================= Команды персонажа =================
                case ICharacterCommand characterCommand:
                    await _policyEnforcer.EnforceControlCharacterAsync(characterCommand.CharacterId, context.CancellationToken);
                    break;

                // ================= Команды боя (мастер кампании) =================
                case StartCombat:
                case EndCombat:
                case RollInitiative:
                case StartRound:
                case NextTurn:
                case EndRound:
                case AddParticipantToCombat:
                case RemoveParticipantFromCombat:
                case TakeMoveAction:
                case TakeStandardAction:
                case TakeBonusAction:
                case TakeReaction:
                case ReadyAction:
                case TriggerReadyAction:
                case DealDamageToTarget:
                case HealTarget:
                case ApplyConditionToTarget:
                case RemoveConditionFromTarget:
                case MakeSavingThrowInCombat:
                case MakeDeathSavingThrowInCombat:
                case StabilizeInCombat:
                case MakeConcentrationCheck:
                case DelayTurn:
                case SurrenderInCombat:
                case PerformAction:
                case HelpAction:
                case HideAction:
                case SearchAction:
                case UseObjectAction:
                    await EnforceCampaignMasterAsync(context);
                    break;

                // ================= Команды кампании =================
                case CreateCampaignCommand:
                    if (!await _permissionChecker.IsGameMasterAsync(context.CancellationToken))
                        throw new UnauthorizedAccessException("Только Мастер или Администратор может создавать кампании.");
                    break;

                case AddPlayerToCampaignCommand:
                case RemovePlayerFromCampaignCommand:
                case CreateQuestCommand:
                case AcceptQuestCommand:
                case CompleteQuestCommand:
                case FailQuestCommand:
                case UpdateQuestObjectiveCommand:
                case ChangeFactionReputationCommand:
                case DeleteQuestCommand:
                    await EnforceCampaignMasterAsync(context);
                    break;

                // ================= Команды путешествий (мастер кампании) =================
                case StartJourneyCommand:
                case EndJourneyCommand:
                case TravelDayCommand:
                case SetTravelPaceCommand:
                case ForcedMarchCommand:
                case NavigationCheckCommand:
                case PartyLostCommand:
                case ConsumeResourcesCommand:
                case RandomEncounterCheckCommand:
                case ApplyExhaustionCommand:
                    await EnforceCampaignMasterAsync(context);
                    break;

                // ================= Команды отдыха (контроль персонажа) =================
                case StartRest:
                case EndRest:
                case SpendHitDie:
                case InterruptRest:
                    await _policyEnforcer.EnforceControlCharacterAsync(GetCharacterId(command), context.CancellationToken);
                    break;

                // ================= Команды перемещения (контроль персонажа) =================
                case MoveCharacter:
                case MoveCharacterToPosition:
                case MoveCharacterWithDash:
                case MoveCharacterWithDisengage:
                case MoveCharacterStealthily:
                case ClimbCharacter:
                case SwimCharacter:
                case FlyCharacter:
                case BurrowCharacter:
                case JumpCharacter:
                case SetCharacterSpeed:
                case ResetCharacterSpeed:
                case ApplyDifficultTerrain:
                case RemoveDifficultTerrain:
                case ApplyMovementImpairment:
                case RemoveMovementImpairment:
                case MakeAthleticsCheckForMovement:
                case MakeAcrobaticsCheckForMovement:
                case TakeFallDamage:
                    await _policyEnforcer.EnforceControlCharacterAsync(GetCharacterId(command), context.CancellationToken);
                    break;

                // ================= Команды инвентаря/золота/состояний (редактирование) =================
                case AddInventoryItem:
                case RemoveInventoryItem:
                case EquipItem:
                case UnequipItem:
                case AddGold:
                case SpendGold:
                case SetGoldCommand:
                case ApplyCondition:
                case RemoveCondition:
                case ClearAllConditionsCommand:
                case ReviveCharacter:
                case ResetDeathSavingThrows:
                case AddResistance:
                case RemoveResistance:
                case AddVulnerability:
                case RemoveVulnerability:
                case AddImmunity:
                case RemoveImmunity:
                case UpdateArmorClass:
                case UpdateSpeed:
                case UpdateProficiencyBonus:
                    await _policyEnforcer.EnforceEditCharacterAsync(GetCharacterId(command), context.CancellationToken);
                    break;

                // ================= Команды изменения данных персонажа (редактирование) =================
                case UpdateCharacter:
                case SetAbilityScore:
                case ChooseRace:
                case ChooseClass:
                case ChooseBackground:
                case AddSkillProficiency:
                case RemoveSkillProficiency:
                case AddSavingThrowProficiency:
                case RemoveSavingThrowProficiency:
                case AddFeat:
                case RemoveFeat:
                case AddSpell:
                case RemoveSpell:
                case PrepareSpell:
                case UnprepareSpell:
                case UseSpellSlot:
                case RestoreAllSpellSlots:
                case GainExperience:
                case LevelUpCharacter:
                    await _policyEnforcer.EnforceEditCharacterAsync(GetCharacterId(command), context.CancellationToken);
                    break;

                // ================= Команды с простым контролем персонажа =================
                case DealDamage:
                case HealCharacter:
                case SetTemporaryHitPoints:
                case DeathSavingThrow:
                case StabilizeCharacter:
                case CastSpell:
                    await _policyEnforcer.EnforceControlCharacterAsync(GetCharacterId(command), context.CancellationToken);
                    break;

                // ================= Прочие команды (пропускаем или доп. логика) =================
                // SpeakCommand, UseItem, LootAll и т.п. могут быть добавлены при необходимости.
                default:
                    // Неизвестные команды пропускаем (или можно запретить)
                    break;
            }

            await next();
        }

        private async Task EnforceCampaignMasterAsync(CommandContext context)
        {
            if (context.GameSessionId == Guid.Empty)
                throw new UnauthorizedAccessException("Не указана игровая сессия (кампания).");

            if (!await _permissionChecker.IsGameMasterOfCampaignAsync(context.GameSessionId, context.CancellationToken))
                throw new UnauthorizedAccessException("Только Мастер кампании может выполнить это действие.");
        }

        private static Guid GetCharacterId(ICommand command)
        {
            return command switch
            {
                // Все команды, у которых есть CharacterId, можно перечислить через интерфейс,
                // но для простоты оставлен switch. Добавьте недостающие по мере необходимости.
                ICharacterCommand c => c.CharacterId,
                StartRest c => c.CharacterId,
                EndRest c => c.CharacterId,
                SpendHitDie c => c.CharacterId,
                InterruptRest c => c.CharacterId,
                MoveCharacter c => c.CharacterId,
                MoveCharacterToPosition c => c.CharacterId,
                MoveCharacterWithDash c => c.CharacterId,
                MoveCharacterWithDisengage c => c.CharacterId,
                MoveCharacterStealthily c => c.CharacterId,
                ClimbCharacter c => c.CharacterId,
                SwimCharacter c => c.CharacterId,
                FlyCharacter c => c.CharacterId,
                BurrowCharacter c => c.CharacterId,
                JumpCharacter c => c.CharacterId,
                SetCharacterSpeed c => c.CharacterId,
                ResetCharacterSpeed c => c.CharacterId,
                ApplyDifficultTerrain c => c.CharacterId,
                RemoveDifficultTerrain c => c.CharacterId,
                ApplyMovementImpairment c => c.CharacterId,
                RemoveMovementImpairment c => c.CharacterId,
                MakeAthleticsCheckForMovement c => c.CharacterId,
                MakeAcrobaticsCheckForMovement c => c.CharacterId,
                TakeFallDamage c => c.CharacterId,
                AddInventoryItem c => c.CharacterId,
                RemoveInventoryItem c => c.CharacterId,
                EquipItem c => c.CharacterId,
                UnequipItem c => c.CharacterId,
                AddGold c => c.CharacterId,
                SpendGold c => c.CharacterId,
                SetGoldCommand c => c.CharacterId,
                ApplyCondition c => c.CharacterId,
                RemoveCondition c => c.CharacterId,
                ClearAllConditionsCommand c => c.CharacterId,
                ReviveCharacter c => c.CharacterId,
                ResetDeathSavingThrows c => c.CharacterId,
                AddResistance c => c.CharacterId,
                RemoveResistance c => c.CharacterId,
                AddVulnerability c => c.CharacterId,
                RemoveVulnerability c => c.CharacterId,
                AddImmunity c => c.CharacterId,
                RemoveImmunity c => c.CharacterId,
                UpdateArmorClass c => c.CharacterId,
                UpdateSpeed c => c.CharacterId,
                UpdateProficiencyBonus c => c.CharacterId,
                UpdateCharacter c => c.CharacterId,
                SetAbilityScore c => c.CharacterId,
                ChooseRace c => c.CharacterId,
                ChooseClass c => c.CharacterId,
                ChooseBackground c => c.CharacterId,
                AddSkillProficiency c => c.CharacterId,
                RemoveSkillProficiency c => c.CharacterId,
                AddSavingThrowProficiency c => c.CharacterId,
                RemoveSavingThrowProficiency c => c.CharacterId,
                AddFeat c => c.CharacterId,
                RemoveFeat c => c.CharacterId,
                AddSpell c => c.CharacterId,
                RemoveSpell c => c.CharacterId,
                PrepareSpell c => c.CharacterId,
                UnprepareSpell c => c.CharacterId,
                UseSpellSlot c => c.CharacterId,
                RestoreAllSpellSlots c => c.CharacterId,
                GainExperience c => c.CharacterId,
                LevelUpCharacter c => c.CharacterId,
                DealDamage c => c.CharacterId,
                HealCharacter c => c.CharacterId,
                SetTemporaryHitPoints c => c.CharacterId,
                DeathSavingThrow c => c.CharacterId,
                StabilizeCharacter c => c.CharacterId,
                CastSpell c => c.CharacterId,
                _ => Guid.Empty
            };
        }

        private static bool RequiresAuthorization(ICommand command)
        {
            // Создание персонажа доступно любому аутентифицированному пользователю
            return command is not CreateCharacter;
        }

        public interface ICharacterCommand : ICommand
        {
            Guid CharacterId { get; }
        }
    }
}