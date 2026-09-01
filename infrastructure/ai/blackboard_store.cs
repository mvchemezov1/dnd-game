#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.ai
{
    /// <summary>
    /// Тип факта в доске объявлений (Blackboard).
    /// </summary>
    public enum FactType
    {
        /// <summary>Глобальное состояние мира (погода, время суток).</summary>
        WorldState,

        /// <summary>Состояние персонажа или существа (жив, локация, хиты).</summary>
        EntityState,

        /// <summary>Отношения между персонажами (союзник, враг).</summary>
        Relationship,

        /// <summary>Произошедшее событие (атака, крик о помощи).</summary>
        Event,

        /// <summary>Информация о месте (опасность, укрытие).</summary>
        Location,

        /// <summary>Информация о предмете (наличие, владелец).</summary>
        Item
    }

    /// <summary>
    /// Отдельный факт на доске объявлений.
    /// </summary>
    public class BlackboardFact
    {
        /// <summary>Идентификатор сущности, к которой относится факт.</summary>
        public Guid EntityId { get; set; }

        /// <summary>Ключ факта (например, "IsAlive", "CurrentLocation").</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Значение факта.</summary>
        public object Value { get; set; } = null!;

        /// <summary>Тип факта.</summary>
        public FactType Type { get; set; }

        /// <summary>Уверенность ИИ в факте (0..1).</summary>
        public float Confidence { get; set; } = 1.0f;

        /// <summary>Время создания/обновления факта (UTC).</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>Срок жизни факта; null — бессрочно.</summary>
        public TimeSpan? Expiration { get; set; }

        /// <summary>Источник факта (кто или что сообщило).</summary>
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// Цель, которую преследует ИИ-существо.
    /// </summary>
    public class BlackboardGoal
    {
        /// <summary>Идентификатор цели (генерируется при создании).</summary>
        public Guid GoalId { get; set; } = Guid.NewGuid();

        /// <summary>Идентификатор сущности, к которой относится цель.</summary>
        public Guid EntityId { get; set; }

        /// <summary>Тип цели (например, "AttackTarget", "MoveToLocation").</summary>
        public string GoalType { get; set; } = string.Empty;

        /// <summary>Параметры цели.</summary>
        public Dictionary<string, object> Parameters { get; set; } = [];

        /// <summary>Приоритет цели (больше — важнее).</summary>
        public int Priority { get; set; }

        /// <summary>Время создания цели (UTC).</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Крайний срок достижения цели; null — нет.</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>Признак завершённости цели.</summary>
        public bool IsCompleted { get; set; }
    }

    /// <summary>
    /// Запись памяти — важное событие для принятия решений.
    /// </summary>
    public class BlackboardMemory
    {
        /// <summary>Идентификатор записи памяти (генерируется при создании).</summary>
        public Guid MemoryId { get; set; } = Guid.NewGuid();

        /// <summary>Идентификатор сущности, которой принадлежит память.</summary>
        public Guid EntityId { get; set; }

        /// <summary>Описание события/факта.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Время записи (UTC).</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>Важность (0..10). Чем выше, тем дольше хранится.</summary>
        public int Importance { get; set; }

        /// <summary>
        /// Продолжительность хранения памяти. Вычисляется на основе важности:
        /// Importance * 10 минут.
        /// </summary>
        public TimeSpan Retention => TimeSpan.FromMinutes(Importance * 10);
    }

    /// <summary>
    /// Интерфейс доски объявлений для ИИ.
    /// </summary>
    public interface IBlackboardStore
    {
        /// <summary>Устанавливает факт для сущности.</summary>
        Task SetFact(
            Guid entityId,
            string key,
            object value,
            FactType type = FactType.EntityState,
            float confidence = 1.0f,
            TimeSpan? expiration = null,
            string source = "");

        /// <summary>Получает факт по ключу и идентификатору сущности.</summary>
        Task<BlackboardFact?> GetFact(Guid entityId, string key);

        /// <summary>Возвращает список фактов, удовлетворяющих фильтрам.</summary>
        Task<List<BlackboardFact>> QueryFacts(
            Guid entityId,
            FactType? type = null,
            float minConfidence = 0.0f);

        /// <summary>Удаляет факт по ключу.</summary>
        Task RemoveFact(Guid entityId, string key);

        /// <summary>Удаляет все факты с истёкшим сроком жизни.</summary>
        Task ClearExpiredFacts();

        /// <summary>Добавляет цель.</summary>
        Task AddGoal(BlackboardGoal goal);

        /// <summary>Возвращает цели сущности (активные по умолчанию).</summary>
        Task<List<BlackboardGoal>> GetGoals(Guid entityId, bool onlyActive = true);

        /// <summary>Обновляет существующую цель.</summary>
        Task UpdateGoal(BlackboardGoal goal);

        /// <summary>Удаляет цель по идентификатору.</summary>
        Task RemoveGoal(Guid goalId);

        /// <summary>Добавляет запись памяти.</summary>
        Task AddMemory(BlackboardMemory memory);

        /// <summary>Возвращает записи памяти сущности с фильтрами.</summary>
        Task<List<BlackboardMemory>> GetMemories(
            Guid entityId,
            int minImportance = 0,
            DateTime? since = null);

        /// <summary>Удаляет все записи памяти с истёкшим сроком хранения.</summary>
        Task ForgetOldMemories();
    }

    /// <summary>
    /// Реализация доски объявлений в памяти (потокобезопасная).
    /// </summary>
    public class BlackboardStore(ILogger<BlackboardStore>? logger = null) : IBlackboardStore
    {
        private readonly ConcurrentDictionary<string, BlackboardFact> _facts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Guid, BlackboardGoal> _goals = new();
        private readonly ConcurrentDictionary<Guid, BlackboardMemory> _memories = new();
        private readonly ILogger<BlackboardStore> _logger = logger ?? NullLogger<BlackboardStore>.Instance;

        private static string BuildFactKey(Guid entityId, string key) => $"{entityId:N}:{key}";

        // ---------- Факты ----------

        public Task SetFact(
            Guid entityId,
            string key,
            object value,
            FactType type = FactType.EntityState,
            float confidence = 1.0f,
            TimeSpan? expiration = null,
            string source = "")
        {
            ValidateEntityId(entityId);
            ValidateKey(key);
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            ValidateConfidence(confidence);
            if (expiration.HasValue && expiration.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(expiration), "Срок жизни факта должен быть положительным.");

            var fact = new BlackboardFact
            {
                EntityId = entityId,
                Key = key,
                Value = value,
                Type = type,
                Confidence = Math.Clamp(confidence, 0f, 1f),
                Timestamp = DateTime.UtcNow,
                Expiration = expiration,
                Source = source ?? string.Empty
            };

            _facts[BuildFactKey(entityId, key)] = fact;
            _logger.LogDebug("Факт установлен: {EntityId}:{Key}", entityId, key);
            return Task.CompletedTask;
        }

        public Task<BlackboardFact?> GetFact(Guid entityId, string key)
        {
            ValidateEntityId(entityId);
            ValidateKey(key);

            if (_facts.TryGetValue(BuildFactKey(entityId, key), out var fact))
            {
                if (IsExpired(fact))
                {
                    _facts.TryRemove(BuildFactKey(entityId, key), out _);
                    return Task.FromResult<BlackboardFact?>(null);
                }
                return Task.FromResult<BlackboardFact?>(fact);
            }
            return Task.FromResult<BlackboardFact?>(null);
        }

        public Task<List<BlackboardFact>> QueryFacts(
            Guid entityId,
            FactType? type = null,
            float minConfidence = 0.0f)
        {
            ValidateEntityId(entityId);
            ValidateConfidence(minConfidence);

            var now = DateTime.UtcNow;
            var result = _facts.Values
                .Where(f => f.EntityId == entityId)
                .Where(f => !type.HasValue || f.Type == type.Value)
                .Where(f => f.Confidence >= minConfidence)
                .Where(f => !IsExpired(f, now))
                .ToList();

            // Удаляем истёкшие факты (необязательно, но поддерживаем чистоту)
            var expired = _facts.Values.Where(f => IsExpired(f, now)).ToList();
            foreach (var f in expired)
            {
                _facts.TryRemove(BuildFactKey(f.EntityId, f.Key), out _);
            }

            return Task.FromResult(result);
        }

        public Task RemoveFact(Guid entityId, string key)
        {
            ValidateEntityId(entityId);
            ValidateKey(key);
            _facts.TryRemove(BuildFactKey(entityId, key), out _);
            return Task.CompletedTask;
        }

        public Task ClearExpiredFacts()
        {
            var now = DateTime.UtcNow;
            var expired = _facts.Values.Where(f => IsExpired(f, now)).ToList();
            foreach (var f in expired)
            {
                _facts.TryRemove(BuildFactKey(f.EntityId, f.Key), out _);
            }
            _logger.LogDebug("Удалено истёкших фактов: {Count}", expired.Count);
            return Task.CompletedTask;
        }

        // ---------- Цели ----------

        public Task AddGoal(BlackboardGoal goal)
        {
            ArgumentNullException.ThrowIfNull(goal, nameof(goal));
            ValidateEntityId(goal.EntityId);
            if (string.IsNullOrWhiteSpace(goal.GoalType))
                throw new ArgumentException("Тип цели не может быть пустым.", nameof(goal));
            if (goal.Priority < 0)
                throw new ArgumentOutOfRangeException(nameof(goal), "Приоритет не может быть отрицательным.");
            if (goal.GoalId == Guid.Empty)
                goal.GoalId = Guid.NewGuid(); // на случай, если не был задан

            _goals[goal.GoalId] = goal;
            _logger.LogDebug("Добавлена цель {GoalId} для {EntityId}", goal.GoalId, goal.EntityId);
            return Task.CompletedTask;
        }

        public Task<List<BlackboardGoal>> GetGoals(Guid entityId, bool onlyActive = true)
        {
            ValidateEntityId(entityId);

            var goals = _goals.Values
                .Where(g => g.EntityId == entityId && (!onlyActive || !g.IsCompleted))
                .OrderByDescending(g => g.Priority)
                .ThenBy(g => g.CreatedAt)
                .ToList();

            return Task.FromResult(goals);
        }

        public Task UpdateGoal(BlackboardGoal goal)
        {
            ArgumentNullException.ThrowIfNull(goal, nameof(goal));
            if (goal.GoalId == Guid.Empty)
                throw new ArgumentException("Идентификатор цели не может быть пустым.", nameof(goal));

            _goals[goal.GoalId] = goal;
            _logger.LogDebug("Обновлена цель {GoalId}", goal.GoalId);
            return Task.CompletedTask;
        }

        public Task RemoveGoal(Guid goalId)
        {
            if (goalId == Guid.Empty)
                throw new ArgumentException("Идентификатор цели не может быть пустым.", nameof(goalId));

            _goals.TryRemove(goalId, out _);
            return Task.CompletedTask;
        }

        // ---------- Память ----------

        public Task AddMemory(BlackboardMemory memory)
        {
            ArgumentNullException.ThrowIfNull(memory, nameof(memory));
            ValidateEntityId(memory.EntityId);
            if (string.IsNullOrWhiteSpace(memory.Description))
                throw new ArgumentException("Описание памяти не может быть пустым.", nameof(memory));
            if (memory.Importance < 0 || memory.Importance > 10)
                throw new ArgumentOutOfRangeException(nameof(memory), "Важность должна быть от 0 до 10.");

            if (memory.MemoryId == Guid.Empty)
                memory.MemoryId = Guid.NewGuid();

            _memories[memory.MemoryId] = memory;
            _logger.LogDebug("Добавлена память {MemoryId} для {EntityId}", memory.MemoryId, memory.EntityId);
            return Task.CompletedTask;
        }

        public Task<List<BlackboardMemory>> GetMemories(
            Guid entityId,
            int minImportance = 0,
            DateTime? since = null)
        {
            ValidateEntityId(entityId);
            if (minImportance < 0 || minImportance > 10)
                throw new ArgumentOutOfRangeException(nameof(minImportance), "Минимальная важность должна быть от 0 до 10.");

            var query = _memories.Values
                .Where(m => m.EntityId == entityId && m.Importance >= minImportance);

            if (since.HasValue)
                query = query.Where(m => m.Timestamp >= since.Value);

            var result = query
                .OrderByDescending(m => m.Importance)
                .ThenByDescending(m => m.Timestamp)
                .ToList();

            // Удаляем просроченные воспоминания
            var now = DateTime.UtcNow;
            var expired = _memories.Values.Where(m => now > m.Timestamp + m.Retention).ToList();
            foreach (var mem in expired)
            {
                _memories.TryRemove(mem.MemoryId, out _);
            }

            return Task.FromResult(result);
        }

        public Task ForgetOldMemories()
        {
            var now = DateTime.UtcNow;
            var expired = _memories.Values.Where(m => now > m.Timestamp + m.Retention).ToList();
            foreach (var mem in expired)
            {
                _memories.TryRemove(mem.MemoryId, out _);
            }
            _logger.LogDebug("Удалено устаревших воспоминаний: {Count}", expired.Count);
            return Task.CompletedTask;
        }

        // ---------- Вспомогательные методы ----------

        private static bool IsExpired(BlackboardFact fact)
            => IsExpired(fact, DateTime.UtcNow);

        private static bool IsExpired(BlackboardFact fact, DateTime now)
            => fact.Expiration.HasValue && now > fact.Timestamp + fact.Expiration.Value;

        private static void ValidateEntityId(Guid entityId)
        {
            if (entityId == Guid.Empty)
                throw new ArgumentException("Идентификатор сущности не может быть пустым.", nameof(entityId));
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Ключ факта не может быть пустым.", nameof(key));
        }

        private static void ValidateConfidence(float confidence)
        {
            if (confidence < 0 || confidence > 1)
                throw new ArgumentOutOfRangeException(nameof(confidence), "Уверенность должна быть от 0 до 1.");
        }
    }
}