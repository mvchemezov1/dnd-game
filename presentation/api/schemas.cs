#nullable enable
using dnd_game.application.security;
using dnd_game.application.services;
using dnd_game.domain.events;

namespace dnd_game.presentation.api
{
    /// <summary>
    /// Контейнер схем данных API (запросы/ответы), используемых в REST-интерфейсе игры.
    /// </summary>
    public static class Schemas
    {
        // =====================================================================
        // Персонажи
        // =====================================================================

        /// <summary>Запрос на создание персонажа.</summary>
        public sealed record CreateCharacterRequest(string Name, int MaxHitPoints, bool IsNpc = false);

        public sealed record MoveCharacterRequest(Guid CharacterId, int TargetX, int TargetY);

        /// <summary>Запрос на обновление персонажа (поля опциональны).</summary>
        public sealed record UpdateCharacterRequest(string? Name, int? MaxHitPoints);

        /// <summary>Запрос на установку значения характеристики.</summary>
        public sealed record SetAbilityScoreRequest(int Score);

        /// <summary>Запрос на добавление золота.</summary>
        public sealed record AddGoldRequest(int Amount);

        /// <summary>Запрос на трату золота.</summary>
        public sealed record SpendGoldRequest(int Amount);

        /// <summary>Запрос на установку точного количества золота (для мастера/админа).</summary>
        public sealed record SetGoldRequest(int Amount);

        /// <summary>Запрос на смену пароля текущим пользователем.</summary>
        public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

        /// <summary>Запрос на восстановление пароля (заглушка).</summary>
        public sealed record ForgotPasswordRequest(string Email);
        public sealed record ResetPasswordRequest(string Token, string NewPassword);

        // =====================================================================
        // Бой
        // =====================================================================

        /// <summary>Запрос на начало боя.</summary>
        public sealed record StartCombatRequest(
        Guid CombatId,
        List<Guid> Participants,
        List<Guid>? PlayerCharacterIds = null);

        /// <summary>Запрос на бросок инициативы участника.</summary>
        public sealed record RollInitiativeRequest(Guid ParticipantId, int InitiativeRoll, int DexterityModifier);

        /// <summary>Запрос на добавление участника в бой.</summary>
        public sealed record AddParticipantRequest(Guid ParticipantId, int Initiative);

        /// <summary>Запрос на перемещение в бою.</summary>
        public sealed record TakeMoveActionRequest(Guid ParticipantId, int DistanceFeet);

        /// <summary>Запрос на стандартное действие.</summary>
        public sealed record TakeStandardActionRequest(Guid ParticipantId, string ActionType, Guid? TargetId = null, object? ActionData = null);

        /// <summary>Запрос на бонусное действие.</summary>
        public sealed record TakeBonusActionRequest(Guid ParticipantId, string ActionType, Guid? TargetId = null, object? ActionData = null);

        /// <summary>Запрос на реакцию.</summary>
        public sealed record TakeReactionRequest(Guid ParticipantId, string ReactionType, string TriggerDescription, Guid? TargetId = null);

        /// <summary>Запрос на подготовку действия.</summary>
        public sealed record ReadyActionRequest(Guid ParticipantId, string ActionToReady, string TriggerCondition);

        /// <summary>Запрос на активацию подготовленного действия.</summary>
        public sealed record TriggerReadyActionRequest(Guid ParticipantId);

        /// <summary>Запрос на нанесение урона цели.</summary>
        public sealed record DealDamageRequest(Guid SourceParticipantId, Guid TargetParticipantId, int DamageAmount, string DamageType);

        /// <summary>Запрос на лечение цели.</summary>
        public sealed record HealTargetRequest(Guid SourceParticipantId, Guid TargetParticipantId, int HealingAmount);

        /// <summary>Запрос на наложение состояния.</summary>
        public sealed record ApplyConditionRequest(Guid TargetParticipantId, string ConditionType, int DurationRounds);

        /// <summary>Запрос на снятие состояния.</summary>
        public sealed record RemoveConditionRequest(Guid TargetParticipantId, string ConditionType);

        /// <summary>Запрос на совершение спасброска.</summary>
        public sealed record MakeSavingThrowRequest(Guid ParticipantId, string Ability, int DifficultyClass, int RollResult, int Modifiers);

        /// <summary>Запрос на спасбросок от смерти.</summary>
        public sealed record MakeDeathSavingThrowRequest(Guid ParticipantId, int RollResult);

        /// <summary>Запрос на стабилизацию участника.</summary>
        public sealed record StabilizeRequest(Guid ParticipantId, Guid StabilizedByParticipantId);

        /// <summary>Запрос на проверку концентрации.</summary>
        public sealed record MakeConcentrationCheckRequest(Guid ParticipantId, int DifficultyClass, int RollResult, int ConstitutionModifier);

        /// <summary>Запрос на откладывание хода.</summary>
        public sealed record DelayTurnRequest(Guid ParticipantId);

        /// <summary>Запрос на сдачу в бою.</summary>
        public sealed record SurrenderRequest(Guid ParticipantId);

        /// <summary>Универсальный запрос на выполнение действия.</summary>
        public sealed record PerformActionRequest(Guid ParticipantId, string ActionType, Guid? TargetId = null, object? ActionData = null);

        // =====================================================================
        // Кампания / Квесты
        // =====================================================================

        /// <summary>Запрос на создание квеста.</summary>
        public sealed record CreateQuestRequest(
            Guid QuestId,
            string Title,
            string Description,
            List<QuestObjectiveData> Objectives,
            List<QuestRewardData> Rewards,
            List<Guid> ParticipantIds);

        /// <summary>Запрос на обновление цели квеста.</summary>
        public sealed record UpdateQuestObjectiveRequest(
            int ObjectiveIndex,
            bool IsCompleted,
            int CurrentProgress);

        /// <summary>Запрос на создание кампании.</summary>
        public sealed record CreateCampaignRequest(
            Guid CampaignId,
            string Name,
            Guid GameMasterId);

        /// <summary>Запрос на добавление игрока в кампанию.</summary>
        public sealed record AddPlayerRequest(Guid PlayerId);

        // =====================================================================
        // Крафтинг
        // =====================================================================

        /// <summary>Запрос на начало крафта.</summary>
        public sealed record StartCraftingRequest(Guid CharacterId, Guid RecipeId);

        /// <summary>Запрос на отмену крафта.</summary>
        public sealed record CancelCraftingRequest(Guid ProcessId);

        // =====================================================================
        // Торговля
        // =====================================================================

        /// <summary>Запрос на создание торгового предложения.</summary>
        public sealed record ProposeTradeRequest(
            Guid FromCharacterId,
            Guid ToCharacterId,
            List<TradeItem> OfferedItems,
            int OfferedGold,
            List<TradeItem> RequestedItems,
            int RequestedGold);

        /// <summary>Запрос на принятие торгового предложения.</summary>
        public sealed record AcceptTradeRequest(Guid OfferId);

        /// <summary>Запрос на отклонение торгового предложения.</summary>
        public sealed record DeclineTradeRequest(Guid OfferId);

        /// <summary>Запрос на отмену торгового предложения.</summary>
        public sealed record CancelTradeOfferRequest(Guid OfferId);

        // =====================================================================
        // Диалоги
        // =====================================================================

        public sealed record ResolveSkillCheckRequest(
        int RollResult,
        int ProficiencyBonus,
        int AbilityModifier);

        /// <summary>Запрос на начало диалога.</summary>
        public sealed record StartDialogRequest(Guid DialogueId, Guid NpcId, Guid CharacterId);

        /// <summary>Запрос на выбор варианта диалога.</summary>
        public sealed record SelectOptionRequest(Guid DialogueId, Guid OptionId);

        /// <summary>Запрос на завершение диалога.</summary>
        public sealed record EndDialogRequest(Guid DialogueId);

        // =====================================================================
        // Путешествия
        // =====================================================================

        /// <summary>Запрос на использование рывка (Dash).</summary>
        public sealed record DashRequest(Guid CharacterId);

        /// <summary>Запрос на специальное перемещение.</summary>
        public sealed record SpecialMovementRequest(Guid CharacterId, int DistanceFeet, string MovementType);

        /// <summary>Запрос на начало путешествия.</summary>
        public sealed record StartJourneyRequest(Guid PartyId, Guid RouteId, TravelPace Pace);

        /// <summary>Запрос на завершение путешествия.</summary>
        public sealed record EndJourneyRequest(Guid PartyId);

        /// <summary>Запрос на прохождение одного дня путешествия.</summary>
        public sealed record TravelDayRequest(Guid PartyId, TerrainType Terrain, int HoursTraveled, int NavigationCheckResult);

        // =====================================================================
        // WebSocket-сообщения (базовые)
        // =====================================================================

        /// <summary>Запрос на выход из системы.</summary>
        public sealed record LogoutRequest(string RefreshToken);

        /// <summary>Базовое сообщение WebSocket.</summary>
        public abstract record WebSocketMessageBase(string Type, string? CorrelationId);

        /// <summary>Запрос аутентификации через WebSocket.</summary>
        public sealed record AuthRequestMessage(string Token) : WebSocketMessageBase("auth", null);

        /// <summary>Ответ аутентификации через WebSocket.</summary>
        public sealed record AuthResponseMessage(bool Success, Guid? UserId, string? Error) : WebSocketMessageBase("auth_response", null);

        /// <summary>Сообщение с командой.</summary>
        public sealed record CommandMessage(string CommandType, string CommandJson) : WebSocketMessageBase("command", null);

        /// <summary>Ответ на команду.</summary>
        public sealed record CommandResponseMessage(bool Success, string? ErrorMessage, string? ResultJson) : WebSocketMessageBase("command_response", null);

        /// <summary>Сообщение с событием.</summary>
        public sealed record EventMessage(string EventType, string EventJson) : WebSocketMessageBase("event", null);

        /// <summary>Сообщение об ошибке.</summary>
        public sealed record ErrorMessage(string ErrorCode, string Message, string? Detail) : WebSocketMessageBase("error", null);

        /// <summary>Пинг.</summary>
        public sealed record PingMessage() : WebSocketMessageBase("ping", null);

        /// <summary>Понг.</summary>
        public sealed record PongMessage() : WebSocketMessageBase("pong", null);

        // =====================================================================
        // Управление пользователями (администратор)
        // =====================================================================

        /// <summary>Запрос на изменение роли пользователя.</summary>
        public sealed record ChangeUserRoleRequest(string Role);

        /// <summary>Запрос на изменение статуса активности пользователя.</summary>
        public sealed record ChangeUserStatusRequest(bool IsActive);

        /// <summary>Запрос на сброс пароля пользователя администратором.</summary>
        public sealed record ResetUserPasswordRequest(string NewPassword);

        /// <summary>Ответ с информацией о пользователе для администратора.</summary>
        public sealed record AdminUserDto(
            Guid Id,
            string Username,
            string Email,
            string GlobalRole,
            bool IsActive,
            DateTime CreatedAt,
            Dictionary<Guid, CampaignRole> CampaignRoles);

        // =====================================================================
        // Создание диалогов (для мастера)
        // =====================================================================

        /// <summary>Запрос на создание нового диалога с корневым узлом.</summary>
        public sealed record CreateDialogueRequest(
            Guid DialogueId,
            string NpcText,
            bool IsExitNode = false,
            List<DialogueOption>? Options = null);

        /// <summary>Запрос на добавление узла к существующему диалогу.</summary>
        public sealed record AddDialogueNodeRequest(
            Guid DialogueId,
            Guid NodeId,
            string NpcText,
            bool IsExitNode = false,
            List<DialogueOption>? Options = null,
            bool IsRoot = false);

        /// <summary>Запрос на установку корневого узла.</summary>
        public sealed record SetDialogueRootRequest(Guid DialogueId, Guid NodeId);
    }
}