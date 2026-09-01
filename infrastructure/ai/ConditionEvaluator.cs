#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.event_handlers;
using dnd_game.application.projections;
using dnd_game.domain.events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace dnd_game.infrastructure.ai
{
    /// <summary>
    /// Реализация <see cref="IConditionEvaluator"/>, которая проверяет условия триггеров
    /// на основе данных из проекций, хранилища фактов и контекста события.
    /// </summary>
    public class ConditionEvaluator : IConditionEvaluator
    {
        private readonly CharacterProjection _characterProjection;
        private readonly CampaignProjection _campaignProjection;
        private readonly CombatProjection _combatProjection;
        private readonly IBlackboardStore _blackboard;
        private readonly ILogger<ConditionEvaluator> _logger;

        public ConditionEvaluator(
            CharacterProjection characterProjection,
            CampaignProjection campaignProjection,
            CombatProjection combatProjection,
            IBlackboardStore blackboard,
            ILogger<ConditionEvaluator>? logger = null)
        {
            _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            _campaignProjection = campaignProjection ?? throw new ArgumentNullException(nameof(campaignProjection));
            _combatProjection = combatProjection ?? throw new ArgumentNullException(nameof(combatProjection));
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            _logger = logger ?? NullLogger<ConditionEvaluator>.Instance;
        }

        public async Task<bool> EvaluateAsync(
            TriggerCondition condition,
            IDomainEvent triggeringEvent,
            CancellationToken cancellationToken)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (triggeringEvent == null) throw new ArgumentNullException(nameof(triggeringEvent));
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return condition.ConditionType.ToLowerInvariant() switch
                {
                    "hasitem" => await EvaluateHasItemAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "minlevel" or "levelgreaterthan" => await EvaluateMinLevelAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "maxlevel" or "levellessthan" => await EvaluateMaxLevelAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "isalive" => await EvaluateIsAliveAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "isdead" => await EvaluateIsDeadAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "hascondition" or "condition" => await EvaluateHasConditionAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "hasgold" or "goldabove" => await EvaluateHasGoldAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "questcompleted" => await EvaluateQuestCompletedAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "questactive" => await EvaluateQuestActiveAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "reputationabove" => await EvaluateReputationAboveAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "reputationbelow" => await EvaluateReputationBelowAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "flagset" or "globalflagequals" => await EvaluateFlagSetAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "incombat" => await EvaluateInCombatAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    "abilityscoreabove" or "abilityabove" => await EvaluateAbilityScoreAboveAsync(condition.Parameters, triggeringEvent, cancellationToken),
                    _ => await EvaluateUnknownAsync(condition, triggeringEvent, cancellationToken)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при оценке условия {ConditionType}", condition.ConditionType);
                return false;
            }
        }

        // ---------------------- Вспомогательные методы ----------------------

        private async Task<bool> EvaluateHasItemAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            var itemId = GetString(parameters, "ItemId") ?? GetString(parameters, "ItemID");
            if (characterId == Guid.Empty || string.IsNullOrEmpty(itemId))
                return false;

            var character = await _characterProjection.GetById(characterId, ct);
            return character?.Inventory?.Any(i => i.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private async Task<bool> EvaluateMinLevelAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            var level = GetInt(parameters, "Value") ?? GetInt(parameters, "MinLevel") ?? 0;
            if (characterId == Guid.Empty || level <= 0)
                return false;

            var character = await _characterProjection.GetById(characterId, ct);
            return character != null && character.Level >= level;
        }

        private async Task<bool> EvaluateMaxLevelAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            var level = GetInt(parameters, "Value") ?? GetInt(parameters, "MaxLevel") ?? int.MaxValue;
            if (characterId == Guid.Empty)
                return false;

            var character = await _characterProjection.GetById(characterId, ct);
            return character != null && character.Level <= level;
        }

        private async Task<bool> EvaluateIsAliveAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            if (characterId == Guid.Empty)
                return false;

            var character = await _characterProjection.GetById(characterId, ct);
            return character != null && character.HitPoints > 0 && !character.IsDead;
        }

        private async Task<bool> EvaluateIsDeadAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            if (characterId == Guid.Empty)
                return false;

            var character = await _characterProjection.GetById(characterId, ct);
            return character?.IsDead == true;
        }

        private async Task<bool> EvaluateHasConditionAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            var condition = GetString(parameters, "Condition") ?? GetString(parameters, "ConditionName");
            if (characterId == Guid.Empty || string.IsNullOrEmpty(condition))
                return false;

            var character = await _characterProjection.GetById(characterId, ct);
            return character?.Conditions?.Contains(condition, StringComparer.OrdinalIgnoreCase) == true;
        }

        private async Task<bool> EvaluateHasGoldAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            var gold = GetInt(parameters, "Gold") ?? GetInt(parameters, "Value") ?? 0;
            if (characterId == Guid.Empty)
                return false;

            var character = await _characterProjection.GetById(characterId, ct);
            return character != null && character.Gold >= gold;
        }

        private async Task<bool> EvaluateQuestCompletedAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var campaignId = GetCampaignId(parameters, triggeringEvent);
            var questId = GetGuid(parameters, "QuestId");
            if (campaignId == Guid.Empty || questId == Guid.Empty)
                return false;

            var quests = await _campaignProjection.GetQuests(campaignId, null, ct);
            var quest = quests.FirstOrDefault(q => q.QuestId == questId);
            return quest?.Status == QuestStatus.Completed;
        }

        private async Task<bool> EvaluateQuestActiveAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var campaignId = GetCampaignId(parameters, triggeringEvent);
            var questId = GetGuid(parameters, "QuestId");
            if (campaignId == Guid.Empty || questId == Guid.Empty)
                return false;

            var quests = await _campaignProjection.GetQuests(campaignId, null, ct);
            var quest = quests.FirstOrDefault(q => q.QuestId == questId);
            return quest?.Status == QuestStatus.Active;
        }

        private async Task<bool> EvaluateReputationAboveAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var factionId = GetString(parameters, "FactionId");
            var threshold = GetInt(parameters, "Value") ?? GetInt(parameters, "MinReputation") ?? 0;
            if (string.IsNullOrEmpty(factionId))
                return false;

            var faction = await _campaignProjection.GetFaction(factionId, ct);
            return faction != null && faction.Reputation >= threshold;
        }

        private async Task<bool> EvaluateReputationBelowAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var factionId = GetString(parameters, "FactionId");
            var threshold = GetInt(parameters, "Value") ?? GetInt(parameters, "MaxReputation") ?? int.MaxValue;
            if (string.IsNullOrEmpty(factionId))
                return false;

            var faction = await _campaignProjection.GetFaction(factionId, ct);
            return faction != null && faction.Reputation <= threshold;
        }

        private async Task<bool> EvaluateFlagSetAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var campaignId = GetCampaignId(parameters, triggeringEvent);
            var flagName = GetString(parameters, "FlagName") ?? GetString(parameters, "Flag");
            var flagValue = GetString(parameters, "FlagValue");
            if (campaignId == Guid.Empty || string.IsNullOrEmpty(flagName))
                return false;

            var state = await _campaignProjection.GetCampaignState(campaignId, ct);
            if (state == null || !state.GlobalFlags.TryGetValue(flagName, out var actualValue))
                return false;

            return flagValue == null || string.Equals(actualValue, flagValue, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> EvaluateInCombatAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            if (characterId == Guid.Empty)
                return false;

            var combatFact = await _blackboard.GetFact(characterId, "CurrentCombatId");
            if (combatFact?.Value is Guid combatId && combatId != Guid.Empty)
            {
                var combat = await _combatProjection.GetStatus(combatId, ct);
                return combat?.IsActive == true;
            }
            return false;
        }

        private async Task<bool> EvaluateAbilityScoreAboveAsync(
            Dictionary<string, object> parameters, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            var characterId = GetCharacterId(parameters, triggeringEvent);
            var ability = GetString(parameters, "Ability");
            var threshold = GetInt(parameters, "Value") ?? GetInt(parameters, "Score") ?? 0;
            if (characterId == Guid.Empty || string.IsNullOrEmpty(ability))
                return false;

            var character = await _characterProjection.GetById(characterId, ct);
            if (character == null)
                return false;

            if (character.AbilityScores.TryGetValue(ability, out var score))
                return score >= threshold;

            var pair = character.AbilityScores.FirstOrDefault(kv =>
                kv.Key.Equals(ability, StringComparison.OrdinalIgnoreCase));
            return pair.Key != null && pair.Value >= threshold;
        }

        private Task<bool> EvaluateUnknownAsync(
            TriggerCondition condition, IDomainEvent triggeringEvent, CancellationToken ct)
        {
            _logger.LogWarning("Неизвестный тип условия триггера: {ConditionType}", condition.ConditionType);
            return Task.FromResult(false);
        }

        // ---------------------- Извлечение параметров ----------------------

        private Guid GetCharacterId(Dictionary<string, object> parameters, IDomainEvent triggeringEvent)
        {
            var id = GetGuid(parameters, "CharacterId");
            if (id != Guid.Empty) return id;

            // Явно используем dnd_game.domain.events.ICharacterEvent
            if (triggeringEvent is dnd_game.domain.events.ICharacterEvent charEvent)
                return charEvent.CharacterId;

            return Guid.Empty;
        }

        private Guid GetCampaignId(Dictionary<string, object> parameters, IDomainEvent triggeringEvent)
        {
            var id = GetGuid(parameters, "CampaignId");
            if (id != Guid.Empty) return id;

            if (triggeringEvent is ICampaignEvent campaignEvent)
                return campaignEvent.CampaignId;

            return Guid.Empty;
        }

        private static Guid GetGuid(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value))
                return Guid.Empty;

            return value switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var parsed) => parsed,
                _ => Guid.Empty
            };
        }

        private static int? GetInt(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value))
                return null;

            return value switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }

        private static string? GetString(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value))
                return null;

            return value?.ToString();
        }
    }
}