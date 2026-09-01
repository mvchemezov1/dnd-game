#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using dnd_game.domain.commands;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.infrastructure.ai
{
    /// <summary>
    /// Типы команд скриптового движка.
    /// </summary>
    public enum ScriptCommandType
    {
        SetVariable,
        If,
        Else,
        EndIf,
        While,
        EndWhile,
        Wait,
        DamageCharacter,
        HealCharacter,
        MoveCharacter,
        GiveItem,
        RemoveItem,
        StartDialogue,
        SetQuestStage,
        CompleteQuest,
        FailQuest,
        ChangeFactionReputation,
        SpawnMonster,
        StartCombat,
        EndCombat,
        ApplyCondition,
        RemoveCondition,
        Teleport,
        PlaySound,
        LogMessage,
        RollSkillCheck,
        SetGlobalFlag,
        RemoveGlobalFlag,
        AdvanceTime,
        ChangeWeather,
        ExecuteCommandBus
    }

    /// <summary>
    /// Одна инструкция скрипта.
    /// </summary>
    public class ScriptCommand
    {
        public ScriptCommandType Type { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = [];
        public List<ScriptCommand> Children { get; set; } = [];
        public int LineNumber { get; set; }
    }

    /// <summary>
    /// Определение скрипта.
    /// </summary>
    public class ScriptDefinition
    {
        public string ScriptName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ScriptCommand> Commands { get; set; } = [];
    }

    /// <summary>
    /// Репозиторий скриптов.
    /// </summary>
    public interface IScriptRepository
    {
        Task<ScriptDefinition?> GetByNameAsync(string scriptName, CancellationToken cancellationToken = default);
        Task AddOrUpdateAsync(ScriptDefinition script, CancellationToken cancellationToken = default);
        Task<List<string>> GetAllScriptNamesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Контекст выполнения скрипта.
    /// </summary>
    public class ScriptExecutionContext
    {
        public Dictionary<string, object> Variables { get; set; } = [];
        public Guid? CurrentCharacterId { get; set; }
        public Guid? CurrentCampaignId { get; set; }
        public IServiceProvider Services { get; set; } = null!;
    }

    /// <summary>
    /// Движок выполнения скриптов для DnD.
    /// </summary>
    public class ScriptEngine(
        IScriptRepository scriptRepository,
        IServiceProvider serviceProvider,
        ILogger<ScriptEngine> logger)
    {
        private readonly IScriptRepository _scriptRepository = scriptRepository ?? throw new ArgumentNullException(nameof(scriptRepository));
        private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        private readonly ILogger<ScriptEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Запускает скрипт по имени с заданным контекстом.
        /// </summary>
        public async Task RunScriptAsync(string scriptName, Dictionary<string, object>? context = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
                throw new ArgumentException("Имя скрипта не может быть пустым.", nameof(scriptName));
            cancellationToken.ThrowIfCancellationRequested();

            var script = await _scriptRepository.GetByNameAsync(scriptName, cancellationToken);
            if (script == null)
            {
                _logger.LogWarning("Скрипт '{ScriptName}' не найден.", scriptName);
                return;
            }

            var execContext = new ScriptExecutionContext
            {
                Variables = context ?? [],
                Services = _serviceProvider
            };

            await ExecuteCommandsAsync(script.Commands, execContext, cancellationToken);
        }

        // --------------------------------------------------------------------------------
        // Выполнение списка команд с поддержкой блоков if/while
        // --------------------------------------------------------------------------------
        private async Task ExecuteCommandsAsync(
            List<ScriptCommand> commands,
            ScriptExecutionContext context,
            CancellationToken ct)
        {
            int index = 0;
            while (index < commands.Count)
            {
                ct.ThrowIfCancellationRequested();
                var cmd = commands[index];
                _logger.LogTrace("Выполняется команда {Type} на строке {Line}", cmd.Type, cmd.LineNumber);

                switch (cmd.Type)
                {
                    case ScriptCommandType.If:
                        index = await ExecuteIfBlockAsync(commands, index, context, ct);
                        continue; // index уже указывает на EndIf
                    case ScriptCommandType.While:
                        index = await ExecuteWhileBlockAsync(commands, index, context, ct);
                        continue;
                    default:
                        await ExecuteSingleCommandAsync(cmd, context, ct);
                        break;
                }
                index++;
            }
        }

        private async Task<int> ExecuteIfBlockAsync(
            List<ScriptCommand> commands,
            int startIndex,
            ScriptExecutionContext context,
            CancellationToken ct)
        {
            var cmd = commands[startIndex];
            bool condition = EvaluateCondition(cmd.Parameters, context);

            // Находим соответствующие Else и EndIf
            int elseIndex = -1;
            int endIfIndex = -1;
            int depth = 1;
            for (int i = startIndex + 1; i < commands.Count; i++)
            {
                if (commands[i].Type == ScriptCommandType.If) depth++;
                else if (commands[i].Type == ScriptCommandType.EndIf)
                {
                    depth--;
                    if (depth == 0) { endIfIndex = i; break; }
                }
                else if (depth == 1 && commands[i].Type == ScriptCommandType.Else)
                {
                    elseIndex = i;
                }
            }

            if (endIfIndex == -1)
            {
                _logger.LogError("Синтаксическая ошибка: отсутствует EndIf для If на строке {Line}", cmd.LineNumber);
                return commands.Count;
            }

            if (condition)
            {
                int stop = elseIndex != -1 ? elseIndex : endIfIndex;
                for (int i = startIndex + 1; i < stop; i++)
                    await ExecuteSingleCommandAsync(commands[i], context, ct);
            }
            else if (elseIndex != -1)
            {
                for (int i = elseIndex + 1; i < endIfIndex; i++)
                    await ExecuteSingleCommandAsync(commands[i], context, ct);
            }

            return endIfIndex;
        }

        private async Task<int> ExecuteWhileBlockAsync(
            List<ScriptCommand> commands,
            int startIndex,
            ScriptExecutionContext context,
            CancellationToken ct)
        {
            var cmd = commands[startIndex];
            int endWhileIndex = -1;
            int depth = 1;
            for (int i = startIndex + 1; i < commands.Count; i++)
            {
                if (commands[i].Type == ScriptCommandType.While) depth++;
                else if (commands[i].Type == ScriptCommandType.EndWhile)
                {
                    depth--;
                    if (depth == 0) { endWhileIndex = i; break; }
                }
            }

            if (endWhileIndex == -1)
            {
                _logger.LogError("Синтаксическая ошибка: отсутствует EndWhile для While на строке {Line}", cmd.LineNumber);
                return commands.Count;
            }

            int iteration = 0;
            const int maxIterations = 1000; // защита от бесконечного цикла
            while (EvaluateCondition(cmd.Parameters, context) && iteration < maxIterations)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = startIndex + 1; i < endWhileIndex; i++)
                {
                    await ExecuteSingleCommandAsync(commands[i], context, ct);
                }
                iteration++;
            }

            if (iteration >= maxIterations)
                _logger.LogWarning("Достигнут лимит итераций цикла While на строке {Line}", cmd.LineNumber);

            return endWhileIndex;
        }

        // --------------------------------------------------------------------------------
        // Выполнение одной команды
        // --------------------------------------------------------------------------------
        private async Task ExecuteSingleCommandAsync(ScriptCommand cmd, ScriptExecutionContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            switch (cmd.Type)
            {
                case ScriptCommandType.SetVariable:
                    ExecuteSetVariable(cmd, context);
                    break;
                case ScriptCommandType.Wait:
                    await ExecuteWaitAsync(cmd, ct);
                    break;
                case ScriptCommandType.DamageCharacter:
                    await ExecuteDamageCharacterAsync(cmd, context);
                    break;
                case ScriptCommandType.HealCharacter:
                    await ExecuteHealCharacterAsync(cmd, context);
                    break;
                case ScriptCommandType.MoveCharacter:
                    await ExecuteMoveCharacterAsync(cmd, context);
                    break;
                case ScriptCommandType.GiveItem:
                    await ExecuteGiveItemAsync(cmd, context);
                    break;
                case ScriptCommandType.RemoveItem:
                    await ExecuteRemoveItemAsync(cmd, context);
                    break;
                case ScriptCommandType.StartDialogue:
                    await ExecuteStartDialogueAsync(cmd, context);
                    break;
                case ScriptCommandType.SetQuestStage:
                    await ExecuteSetQuestStageAsync(cmd, context);
                    break;
                case ScriptCommandType.CompleteQuest:
                    await ExecuteCompleteQuestAsync(cmd, context);
                    break;
                case ScriptCommandType.FailQuest:
                    await ExecuteFailQuestAsync(cmd, context);
                    break;
                case ScriptCommandType.ChangeFactionReputation:
                    await ExecuteChangeFactionReputationAsync(cmd, context);
                    break;
                case ScriptCommandType.SpawnMonster:
                    await ExecuteSpawnMonsterAsync(cmd, context);
                    break;
                case ScriptCommandType.StartCombat:
                    await ExecuteStartCombatAsync(cmd, context);
                    break;
                case ScriptCommandType.EndCombat:
                    await ExecuteEndCombatAsync(cmd, context);
                    break;
                case ScriptCommandType.ApplyCondition:
                    await ExecuteApplyConditionAsync(cmd, context);
                    break;
                case ScriptCommandType.RemoveCondition:
                    await ExecuteRemoveConditionAsync(cmd, context);
                    break;
                case ScriptCommandType.Teleport:
                    await ExecuteTeleportAsync(cmd, context);
                    break;
                case ScriptCommandType.PlaySound:
                    ExecutePlaySound(cmd);
                    break;
                case ScriptCommandType.LogMessage:
                    ExecuteLogMessage(cmd);
                    break;
                case ScriptCommandType.RollSkillCheck:
                    await ExecuteRollSkillCheckAsync(cmd, context);
                    break;
                case ScriptCommandType.SetGlobalFlag:
                    await ExecuteSetGlobalFlagAsync(cmd, context);
                    break;
                case ScriptCommandType.RemoveGlobalFlag:
                    await ExecuteRemoveGlobalFlagAsync(cmd, context);
                    break;
                case ScriptCommandType.AdvanceTime:
                    await ExecuteAdvanceTimeAsync(cmd, context);
                    break;
                case ScriptCommandType.ChangeWeather:
                    await ExecuteChangeWeatherAsync(cmd, context);
                    break;
                case ScriptCommandType.ExecuteCommandBus:
                    await ExecuteCommandBusCmdAsync(cmd, context);
                    break;
                default:
                    _logger.LogWarning("Неизвестный тип команды: {Type}", cmd.Type);
                    break;
            }
        }

        // --------------------------------------------------------------------------------
        // Реализация команд
        // --------------------------------------------------------------------------------
        private void ExecuteSetVariable(ScriptCommand cmd, ScriptExecutionContext context)
        {
            if (cmd.Parameters.TryGetValue("Name", out var name) && cmd.Parameters.TryGetValue("Value", out var value))
                context.Variables[name] = value;
            else
                _logger.LogWarning("Команда SetVariable требует параметры Name и Value");
        }

        private static async Task ExecuteWaitAsync(ScriptCommand cmd, CancellationToken ct)
        {
            if (cmd.Parameters.TryGetValue("Milliseconds", out var msStr) && int.TryParse(msStr, out int ms))
                await Task.Delay(ms, ct);
        }

        private async Task ExecuteDamageCharacterAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var targetId = ResolveParameter("TargetId", cmd, context);
            var amount = ResolveInt("Amount", cmd, context, 0);
            var damageType = ResolveParameter("DamageType", cmd, context) ?? "bludgeoning";
            if (targetId == null || !Guid.TryParse(targetId, out var targetGuid))
            {
                _logger.LogWarning("Некорректный TargetId для DamageCharacter");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new DealDamage(targetGuid, amount, damageType));
        }

        private async Task ExecuteHealCharacterAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var targetId = ResolveParameter("TargetId", cmd, context);
            var amount = ResolveInt("Amount", cmd, context, 0);
            if (targetId == null || !Guid.TryParse(targetId, out var targetGuid))
            {
                _logger.LogWarning("Некорректный TargetId для HealCharacter");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new HealCharacter(targetGuid, amount));
        }

        private async Task ExecuteMoveCharacterAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var characterId = ResolveParameter("CharacterId", cmd, context);
            var x = ResolveInt("X", cmd, context, 0);
            var y = ResolveInt("Y", cmd, context, 0);
            if (characterId == null || !Guid.TryParse(characterId, out var charGuid))
            {
                _logger.LogWarning("Некорректный CharacterId для MoveCharacter");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new MoveCharacter(charGuid, x, y));
        }

        private async Task ExecuteGiveItemAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var characterId = ResolveParameter("CharacterId", cmd, context);
            var itemId = ResolveParameter("ItemId", cmd, context);
            var itemName = ResolveParameter("ItemName", cmd, context) ?? "Неизвестный предмет";
            var quantity = ResolveInt("Quantity", cmd, context, 1);
            if (characterId == null || itemId == null || !Guid.TryParse(characterId, out var charGuid))
            {
                _logger.LogWarning("Некорректные параметры для GiveItem");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new AddInventoryItem(charGuid, itemId, itemName, quantity));
        }

        private async Task ExecuteRemoveItemAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var characterId = ResolveParameter("CharacterId", cmd, context);
            var itemId = ResolveParameter("ItemId", cmd, context);
            var quantity = ResolveInt("Quantity", cmd, context, 1);
            if (characterId == null || itemId == null || !Guid.TryParse(characterId, out var charGuid))
            {
                _logger.LogWarning("Некорректные параметры для RemoveItem");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new RemoveInventoryItem(charGuid, itemId, quantity));
        }

        private async Task ExecuteStartDialogueAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var dialogueId = ResolveParameter("DialogueId", cmd, context);
            var npcId = ResolveParameter("NpcId", cmd, context);
            var characterId = ResolveParameter("CharacterId", cmd, context);
            if (dialogueId == null || npcId == null || characterId == null ||
                !Guid.TryParse(dialogueId, out var dGuid) ||
                !Guid.TryParse(npcId, out var nGuid) ||
                !Guid.TryParse(characterId, out var cGuid))
            {
                _logger.LogWarning("Некорректные параметры для StartDialogue");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new StartDialogueCommand(dGuid, nGuid, cGuid));
        }

        private async Task ExecuteSetQuestStageAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var campaignId = ResolveParameter("CampaignId", cmd, context) ?? context.CurrentCampaignId?.ToString();
            var questId = ResolveParameter("QuestId", cmd, context);
            var objectiveIndex = ResolveInt("ObjectiveIndex", cmd, context, 0);
            var isCompleted = ResolveBool("IsCompleted", cmd, context, false);
            var progress = ResolveInt("Progress", cmd, context, 0);
            if (campaignId == null || questId == null ||
                !Guid.TryParse(campaignId, out var campGuid) ||
                !Guid.TryParse(questId, out var questGuid))
            {
                _logger.LogWarning("Некорректные параметры для SetQuestStage");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new UpdateQuestObjectiveCommand(campGuid, questGuid, objectiveIndex, isCompleted, progress));
        }

        private async Task ExecuteCompleteQuestAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var campaignId = ResolveParameter("CampaignId", cmd, context) ?? context.CurrentCampaignId?.ToString();
            var questId = ResolveParameter("QuestId", cmd, context);
            if (campaignId == null || questId == null ||
                !Guid.TryParse(campaignId, out var campGuid) ||
                !Guid.TryParse(questId, out var questGuid))
            {
                _logger.LogWarning("Некорректные параметры для CompleteQuest");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new CompleteQuestCommand(campGuid, questGuid));
        }

        private async Task ExecuteFailQuestAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var campaignId = ResolveParameter("CampaignId", cmd, context) ?? context.CurrentCampaignId?.ToString();
            var questId = ResolveParameter("QuestId", cmd, context);
            if (campaignId == null || questId == null ||
                !Guid.TryParse(campaignId, out var campGuid) ||
                !Guid.TryParse(questId, out var questGuid))
            {
                _logger.LogWarning("Некорректные параметры для FailQuest");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new FailQuestCommand(campGuid, questGuid));
        }

        private async Task ExecuteChangeFactionReputationAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var factionId = ResolveParameter("FactionId", cmd, context);
            var change = ResolveInt("Change", cmd, context, 0);
            var campaignIdStr = ResolveParameter("CampaignId", cmd, context)
                                ?? context.CurrentCampaignId?.ToString()
                                ?? Guid.Empty.ToString();
            if (!Guid.TryParse(campaignIdStr, out var campaignId))
                campaignId = Guid.Empty;
            if (factionId == null)
            {
                _logger.LogWarning("Некорректные параметры для ChangeFactionReputation");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new ChangeFactionReputationCommand(campaignId, factionId, change));
        }

        private async Task ExecuteSpawnMonsterAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var templateId = ResolveParameter("TemplateId", cmd, context);
            var x = ResolveInt("X", cmd, context, 0);
            var y = ResolveInt("Y", cmd, context, 0);
            if (templateId == null)
            {
                _logger.LogWarning("Некорректные параметры для SpawnMonster");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new SpawnMonsterCommand(templateId, x, y));
        }

        private async Task ExecuteStartCombatAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var participantsStr = ResolveParameter("Participants", cmd, context);
            var participants = new List<Guid>();
            if (!string.IsNullOrWhiteSpace(participantsStr))
            {
                foreach (var part in participantsStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var g))
                        participants.Add(g);
                }
            }
            var combatId = Guid.NewGuid();
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new StartCombat(combatId, participants));
        }

        private async Task ExecuteEndCombatAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var combatId = ResolveParameter("CombatId", cmd, context);
            if (combatId == null || !Guid.TryParse(combatId, out var cGuid))
            {
                _logger.LogWarning("Некорректный CombatId для EndCombat");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new EndCombat(cGuid));
        }

        private async Task ExecuteApplyConditionAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var targetId = ResolveParameter("TargetId", cmd, context);
            var condition = ResolveParameter("Condition", cmd, context);
            var duration = ResolveInt("DurationRounds", cmd, context, 0);
            if (targetId == null || condition == null || !Guid.TryParse(targetId, out var targetGuid))
            {
                _logger.LogWarning("Некорректные параметры для ApplyCondition");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new ApplyCondition(targetGuid, condition, duration));
        }

        private async Task ExecuteRemoveConditionAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var targetId = ResolveParameter("TargetId", cmd, context);
            var condition = ResolveParameter("Condition", cmd, context);
            if (targetId == null || condition == null || !Guid.TryParse(targetId, out var targetGuid))
            {
                _logger.LogWarning("Некорректные параметры для RemoveCondition");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new RemoveCondition(targetGuid, condition));
        }

        private async Task ExecuteTeleportAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var characterId = ResolveParameter("CharacterId", cmd, context);
            var x = ResolveInt("X", cmd, context, 0);
            var y = ResolveInt("Y", cmd, context, 0);
            if (characterId == null || !Guid.TryParse(characterId, out var charGuid))
            {
                _logger.LogWarning("Некорректный CharacterId для Teleport");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new TeleportCommand(charGuid, x, y));
        }

        private void ExecutePlaySound(ScriptCommand cmd)
        {
            var soundName = cmd.Parameters.GetValueOrDefault("SoundName", "ding");
            _logger.LogInformation("Воспроизведение звука: {SoundName}", soundName);
        }

        private void ExecuteLogMessage(ScriptCommand cmd)
        {
            var message = cmd.Parameters.GetValueOrDefault("Message", "");
            _logger.LogInformation("Сообщение скрипта: {Message}", message);
        }

        private async Task ExecuteRollSkillCheckAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var characterId = ResolveParameter("CharacterId", cmd, context);
            var skill = ResolveParameter("Skill", cmd, context);
            var dc = ResolveInt("DC", cmd, context, 10);
            var resultVar = ResolveParameter("ResultVar", cmd, context) ?? "skillResult";
            var modifier = ResolveInt("Modifier", cmd, context, 0);

            int roll = Random.Shared.Next(1, 21);
            int total = roll + modifier;
            bool success = total >= dc;

            context.Variables[resultVar] = success;
            context.Variables[$"{resultVar}_roll"] = roll;
            context.Variables[$"{resultVar}_total"] = total;
            context.Variables[$"{resultVar}_dc"] = dc;

            _logger.LogDebug("Проверка навыка {Skill} для {Character}: бросок {Roll}+{Modifier}={Total} против DC {DC}, успех={Success}",
                skill, characterId, roll, modifier, total, dc, success);

            await Task.CompletedTask;
        }

        private async Task ExecuteSetGlobalFlagAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var campaignId = ResolveParameter("CampaignId", cmd, context) ?? context.CurrentCampaignId?.ToString();
            var flagName = ResolveParameter("FlagName", cmd, context);
            var flagValue = ResolveParameter("FlagValue", cmd, context) ?? "true";
            if (campaignId == null || flagName == null || !Guid.TryParse(campaignId, out var campGuid))
            {
                _logger.LogWarning("Некорректные параметры для SetGlobalFlag");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new SetGlobalFlagCommand(campGuid, flagName, flagValue));
        }

        private async Task ExecuteRemoveGlobalFlagAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var campaignId = ResolveParameter("CampaignId", cmd, context) ?? context.CurrentCampaignId?.ToString();
            var flagName = ResolveParameter("FlagName", cmd, context);
            if (campaignId == null || flagName == null || !Guid.TryParse(campaignId, out var campGuid))
            {
                _logger.LogWarning("Некорректные параметры для RemoveGlobalFlag");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new RemoveGlobalFlagCommand(campGuid, flagName));
        }

        private async Task ExecuteAdvanceTimeAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var campaignId = ResolveParameter("CampaignId", cmd, context) ?? context.CurrentCampaignId?.ToString();
            var minutes = ResolveInt("Minutes", cmd, context, 60);
            if (campaignId == null || !Guid.TryParse(campaignId, out var campGuid))
            {
                _logger.LogWarning("Некорректный CampaignId для AdvanceTime");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new AdvanceTimeCommand(campGuid, minutes));
        }

        private async Task ExecuteChangeWeatherAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var campaignId = ResolveParameter("CampaignId", cmd, context) ?? context.CurrentCampaignId?.ToString();
            var weather = ResolveParameter("Weather", cmd, context) ?? "Ясно";
            if (campaignId == null || !Guid.TryParse(campaignId, out var campGuid))
            {
                _logger.LogWarning("Некорректный CampaignId для ChangeWeather");
                return;
            }
            var bus = GetCommandBus();
            if (bus != null)
                await bus.SendAsync(new ChangeWeatherCommand(campGuid, weather));
        }

        private async Task ExecuteCommandBusCmdAsync(ScriptCommand cmd, ScriptExecutionContext context)
        {
            var commandBus = _serviceProvider.GetService<ICommandBus>();
            if (commandBus == null)
            {
                _logger.LogWarning("ICommandBus не зарегистрирован в DI.");
                return;
            }

            // 1. Определяем тип команды
            if (!cmd.Parameters.TryGetValue("CommandType", out var commandTypeName) || string.IsNullOrWhiteSpace(commandTypeName))
            {
                _logger.LogWarning("Команда ExecuteCommandBus требует параметр 'CommandType'.");
                return;
            }

            Type? commandType = FindCommandType(commandTypeName);
            if (commandType == null)
            {
                _logger.LogWarning("Тип команды '{CommandType}' не найден.", commandTypeName);
                return;
            }

            // 2. Получаем конструктор
            var constructor = commandType.GetConstructors().FirstOrDefault()
                              ?? throw new InvalidOperationException($"У команды '{commandTypeName}' нет публичного конструктора.");
            var parameters = constructor.GetParameters();

            // 3. Резолвим аргументы конструктора через контекст
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                // Используем ResolveParameter для подстановки значений из variables
                var rawValue = ResolveParameter(param.Name!, cmd, context);
                if (rawValue == null)
                {
                    // Если параметр отсутствует, пробуем взять из CurrentCharacterId/CurrentCampaignId
                    if (param.ParameterType == typeof(Guid) && context.CurrentCharacterId.HasValue)
                        rawValue = context.CurrentCharacterId.Value.ToString();
                    else if (param.ParameterType == typeof(Guid) && context.CurrentCampaignId.HasValue)
                        rawValue = context.CurrentCampaignId.Value.ToString();
                    else
                    {
                        _logger.LogWarning("Для команды '{CommandType}' отсутствует параметр '{ParamName}'.", commandTypeName, param.Name);
                        return;
                    }
                }

                // 4. Конвертируем строку в тип параметра
                var converted = ConvertValue(rawValue, param.ParameterType);
                if (converted == null && param.ParameterType != typeof(string) && !param.ParameterType.IsGenericType)
                {
                    _logger.LogWarning("Не удалось преобразовать '{ParamName}' в тип {ParamType}.", param.Name, param.ParameterType.Name);
                    return;
                }
                args[i] = converted;
            }

            // 5. Создаём команду и отправляем
            try
            {
                var command = Activator.CreateInstance(commandType, args);
                if (command is not ICommand domainCommand)
                {
                    _logger.LogWarning("Тип '{CommandType}' не реализует ICommand.", commandTypeName);
                    return;
                }
                await commandBus.SendAsync(domainCommand);
                _logger.LogDebug("Команда {CommandType} выполнена через ExecuteCommandBus.", commandTypeName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения команды {CommandType}.", commandTypeName);
            }
        }

        /// <summary>
        /// Находит тип команды по имени в сборке domain.commands (сначала полное имя, затем простое имя).
        /// </summary>
        private static Type? FindCommandType(string commandTypeName)
        {
            // Пытаемся найти тип с полным именем или в известном пространстве имён
            var fullName = $"dnd_game.domain.commands.{commandTypeName}";
            var type = Type.GetType(fullName);
            if (type != null) return type;

            // Если не нашли по полному имени, ищем среди всех загруженных типов, реализующих ICommand
            var commandInterface = typeof(ICommand);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var candidate = assembly.GetTypes()
                        .FirstOrDefault(t => t.Name.Equals(commandTypeName, StringComparison.OrdinalIgnoreCase)
                                             && commandInterface.IsAssignableFrom(t));
                    if (candidate != null) return candidate;
                }
                catch
                {
                    // Игнорируем сборки, которые не удаётся просмотреть (например, динамические)
                }
            }
            return null;
        }

        /// <summary>
        /// Преобразует строковое значение в целевой тип (Guid, int, string, bool, List&lt;Guid&gt; и т.п.).
        /// </summary>
        private static object? ConvertValue(string rawValue, Type targetType)
        {
            // Обработка nullable
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                if (string.IsNullOrWhiteSpace(rawValue) || rawValue.Equals("null", StringComparison.OrdinalIgnoreCase))
                    return null;
                return ConvertValue(rawValue, underlyingType);
            }

            if (targetType == typeof(Guid))
                return Guid.Parse(rawValue);
            if (targetType == typeof(string))
                return rawValue;
            if (targetType == typeof(int))
                return int.Parse(rawValue);
            if (targetType == typeof(long))
                return long.Parse(rawValue);
            if (targetType == typeof(bool))
                return bool.Parse(rawValue);
            if (targetType == typeof(double))
                return double.Parse(rawValue);
            if (targetType == typeof(float))
                return float.Parse(rawValue);
            if (targetType.IsEnum)
                return Enum.Parse(targetType, rawValue, true);
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = targetType.GetGenericArguments()[0];
                var items = rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => ConvertValue(s.Trim(), elementType))
                                    .ToArray();
                var list = Activator.CreateInstance(targetType);
                var addMethod = targetType.GetMethod("Add");
                foreach (var item in items)
                    addMethod?.Invoke(list, [item]);
                return list;
            }
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                // Для словарей ожидаем формат "key1:value1,key2:value2"
                var keyType = targetType.GetGenericArguments()[0];
                var valueType = targetType.GetGenericArguments()[1];
                var dict = Activator.CreateInstance(targetType);
                var addMethod = targetType.GetMethod("Add");
                if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    var pairs = rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var pair in pairs)
                    {
                        var kv = pair.Split(':', 2);
                        if (kv.Length == 2)
                        {
                            var key = ConvertValue(kv[0].Trim(), keyType);
                            var value = ConvertValue(kv[1].Trim(), valueType);
                            addMethod?.Invoke(dict, [key, value]);
                        }
                    }
                }
                return dict;
            }

            throw new NotSupportedException($"Преобразование в тип '{targetType.Name}' не поддерживается.");
        }

        // --------------------------------------------------------------------------------
        // Вспомогательные методы
        // --------------------------------------------------------------------------------
        private ICommandBus? GetCommandBus()
        {
            var bus = _serviceProvider.GetService<ICommandBus>();
            if (bus == null)
                _logger.LogWarning("ICommandBus не зарегистрирован в DI.");
            return bus;
        }

        private static string? ResolveParameter(string key, ScriptCommand cmd, ScriptExecutionContext context)
        {
            if (!cmd.Parameters.TryGetValue(key, out var value))
                return null;

            // Если значение начинается с '$', это ссылка на переменную
            if (value.StartsWith('$') && context.Variables.TryGetValue(value[1..], out var varValue))
                return varValue?.ToString();

            return value;
        }

        private static int ResolveInt(string key, ScriptCommand cmd, ScriptExecutionContext context, int defaultValue)
        {
            var val = ResolveParameter(key, cmd, context);
            return val != null && int.TryParse(val, out int result) ? result : defaultValue;
        }

        private static bool ResolveBool(string key, ScriptCommand cmd, ScriptExecutionContext context, bool defaultValue)
        {
            var val = ResolveParameter(key, cmd, context);
            return val != null && bool.TryParse(val, out bool result) ? result : defaultValue;
        }

        private bool EvaluateCondition(Dictionary<string, string> parameters, ScriptExecutionContext context)
        {
            if (!parameters.TryGetValue("Left", out var left) ||
                !parameters.TryGetValue("Op", out var op) ||
                !parameters.TryGetValue("Right", out var right))
            {
                _logger.LogWarning("Условие не содержит обязательных параметров Left, Op, Right");
                return false;
            }

            object? leftVal = ResolveValue(left, context);
            object? rightVal = ResolveValue(right, context);

            if (leftVal is string lStr && rightVal is string rStr)
            {
                return op switch
                {
                    "==" => lStr == rStr,
                    "!=" => lStr != rStr,
                    _ => false
                };
            }

            if (leftVal is int lInt && rightVal is int rInt)
            {
                return op switch
                {
                    "==" => lInt == rInt,
                    "!=" => lInt != rInt,
                    ">" => lInt > rInt,
                    "<" => lInt < rInt,
                    ">=" => lInt >= rInt,
                    "<=" => lInt <= rInt,
                    _ => false
                };
            }

            return false;
        }

        private static object? ResolveValue(string expr, ScriptExecutionContext context)
        {
            if (context.Variables.TryGetValue(expr, out var val))
                return val;

            if (int.TryParse(expr, out int i)) return i;
            if (Guid.TryParse(expr, out Guid g)) return g;
            return expr; // строка
        }
    }
}