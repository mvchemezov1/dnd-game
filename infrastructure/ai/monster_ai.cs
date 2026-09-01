#nullable enable
using dnd_game.application.projections;
using dnd_game.application.security;
using dnd_game.domain.value_objects;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.ai
{
    /// <summary>
    /// Искусственный интеллект для монстров и неигровых персонажей.
    /// Принимает решения о действиях в бою и вне боя, используя доску объявлений (Blackboard),
    /// тактические правила DnD 5e и информацию о текущем состоянии сцены.
    /// </summary>
    public class MonsterAi(
        IBlackboardStore blackboard,
        CharacterProjection characterProjection,
        CombatProjection combatProjection,
        ICommandBus commandBus,
        ICharacterOwnershipRepository ownershipRepository,
        ILogger<MonsterAi>? logger = null)
    {
        private readonly IBlackboardStore _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        private readonly CharacterProjection _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
        private readonly CombatProjection _combatProjection = combatProjection ?? throw new ArgumentNullException(nameof(combatProjection));
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly ICharacterOwnershipRepository _ownershipRepository = ownershipRepository ?? throw new ArgumentNullException(nameof(ownershipRepository));
        private readonly ILogger<MonsterAi> _logger = logger ?? NullLogger<MonsterAi>.Instance;

        // Пороги здоровья для смены тактики (в долях от максимума)
        private const float LowHealthThreshold = 0.25f;
        private const float CriticalHealthThreshold = 0.10f;

        // Дальность ближнего боя по умолчанию (5 футов)
        private const int MeleeRangeFeet = 5;

        /// <summary>
        /// Принимает решение о действии для монстра в текущей ситуации.
        /// Возвращает объект <see cref="MonsterDecision"/> с выбранным действием.
        /// </summary>
        /// <param name="monsterId">Идентификатор монстра.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Решение, которое следует выполнить.</returns>
        public async Task<MonsterDecision> DecideAction(Guid monsterId, CancellationToken cancellationToken = default)
        {
            if (monsterId == Guid.Empty)
                throw new ArgumentException("Идентификатор монстра не может быть пустым.", nameof(monsterId));
            cancellationToken.ThrowIfCancellationRequested();

            // Удаляем устаревшие факты с доски
            await _blackboard.ClearExpiredFacts();

            // Получаем состояние монстра
            var monster = await _characterProjection.GetById(monsterId, cancellationToken);
            if (monster == null || monster.IsDead || monster.IsUnconscious)
            {
                _logger.LogDebug("Монстр {MonsterId} недееспособен. Решение: ничего не делать.", monsterId);
                return MonsterDecision.DoNothing("Монстр недееспособен (мёртв или без сознания).");
            }

            // Определяем текущий бой, если он есть
            CombatStatusDto? combat = null;
            var combatFact = await _blackboard.GetFact(monsterId, "CurrentCombatId");
            if (combatFact?.Value is Guid combatId && combatId != Guid.Empty)
            {
                combat = await _combatProjection.GetStatus(combatId, cancellationToken);
            }

            // Обновляем знания о мире на доске
            await UpdateWorldKnowledge(monsterId, monster, combat, cancellationToken);

            // Проверяем активные цели
            var goals = await _blackboard.GetGoals(monsterId, onlyActive: true);
            if (goals.Count > 0)
            {
                var topGoal = goals[0]; // цели уже отсортированы по убыванию приоритета
                var goalDecision = await PursueGoal(monsterId, topGoal, cancellationToken);
                if (goalDecision != null)
                {
                    _logger.LogDebug("Монстр {MonsterId} следует цели {GoalType}", monsterId, topGoal.GoalType);
                    return goalDecision;
                }
            }

            // Если монстр в активном бою — принимаем боевое решение
            if (combat != null && combat.IsActive)
            {
                return await DecideCombatAction(monsterId, monster, combat, cancellationToken);
            }

            // Вне боя — базовое поведение (патрулирование)
            return await DecideOutOfCombatAction(monsterId, monster, cancellationToken);
        }

        // --------------------------------------------------------------------------------
        // Обновление знаний
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Обновляет факты на доске: собственное состояние монстра, информацию о противниках и оценку угроз.
        /// </summary>
        private async Task UpdateWorldKnowledge(
            Guid monsterId,
            CharacterDto monster,
            CombatStatusDto? combat,
            CancellationToken ct)
        {
            // Собственное состояние
            await _blackboard.SetFact(monsterId, "HitPoints", monster.HitPoints, FactType.EntityState, expiration: TimeSpan.FromSeconds(30));
            await _blackboard.SetFact(monsterId, "MaxHitPoints", monster.MaxHitPoints, FactType.EntityState, expiration: TimeSpan.FromMinutes(5));
            await _blackboard.SetFact(monsterId, "Conditions", monster.Conditions ?? [], FactType.EntityState, expiration: TimeSpan.FromSeconds(15));

            if (combat == null) return;

            await _blackboard.SetFact(monsterId, "CurrentCombatId", combat.CombatId, FactType.EntityState, expiration: TimeSpan.FromSeconds(10));

            // Определяем, является ли монстр NPC (для классификации врагов)
            bool monsterIsNpc = await _ownershipRepository.IsNonPlayerCharacterAsync(monsterId, ct);

            var monsterPosition = new Position(monster.PositionX, monster.PositionY);

            foreach (var participant in combat.Participants)
            {
                if (participant.CharacterId == monsterId) continue;

                var targetChar = await _characterProjection.GetById(participant.CharacterId, ct);
                if (targetChar == null) continue;

                bool targetIsNpc = await _ownershipRepository.IsNonPlayerCharacterAsync(participant.CharacterId, ct);
                // Враг, если монстр NPC и цель игрок (или наоборот)
                bool isEnemy = monsterIsNpc ? !targetIsNpc : targetIsNpc;

                string relation = isEnemy ? "Enemy" : "Ally";
                await _blackboard.SetFact(
                    monsterId,
                    $"Target_{participant.CharacterId}_Relation",
                    relation,
                    FactType.Relationship,
                    expiration: TimeSpan.FromMinutes(1));

                await _blackboard.SetFact(
                    monsterId,
                    $"Target_{participant.CharacterId}_HP",
                    targetChar.HitPoints,
                    FactType.EntityState,
                    expiration: TimeSpan.FromSeconds(10));

                await _blackboard.SetFact(
                    monsterId,
                    $"Target_{participant.CharacterId}_MaxHP",
                    targetChar.MaxHitPoints,
                    FactType.EntityState,
                    expiration: TimeSpan.FromSeconds(10));

                await _blackboard.SetFact(
                    monsterId,
                    $"Target_{participant.CharacterId}_AC",
                    targetChar.ArmorClass,
                    FactType.EntityState,
                    expiration: TimeSpan.FromSeconds(10));

                // Вычисляем и сохраняем дистанцию
                var targetPosition = new Position(targetChar.PositionX, targetChar.PositionY);
                int distanceFeet = monsterPosition.ChebyshevDistanceInFeet(targetPosition);
                await _blackboard.SetFact(
                    monsterId,
                    $"Target_{participant.CharacterId}_Distance",
                    distanceFeet,
                    FactType.Location,
                    expiration: TimeSpan.FromSeconds(10));
            }

            await EvaluateThreats(monsterId, combat, ct);
        }

        /// <summary>
        /// Оценивает угрозы от врагов и выбирает основную цель.
        /// </summary>
        private async Task EvaluateThreats(Guid monsterId, CombatStatusDto combat, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var threats = new List<(Guid CharacterId, float ThreatScore)>();

            foreach (var p in combat.Participants)
            {
                if (p.CharacterId == monsterId) continue;

                var relationFact = await _blackboard.GetFact(monsterId, $"Target_{p.CharacterId}_Relation");
                if (relationFact?.Value?.ToString() != "Enemy") continue;

                // Получаем дистанцию из факта (она была сохранена в UpdateWorldKnowledge)
                var distanceFact = await _blackboard.GetFact(monsterId, $"Target_{p.CharacterId}_Distance");
                int distanceFeet = distanceFact?.Value is int dist ? dist : int.MaxValue;

                int enemyHp = await GetEnemyHitPoints(monsterId, p.CharacterId);
                int enemyMaxHp = await GetEnemyMaxHitPoints(monsterId, p.CharacterId);
                float hpPercent = enemyMaxHp > 0 ? (float)enemyHp / enemyMaxHp : 1f;

                // Угроза выше для близких целей с низким здоровьем (добивание)
                float threat = (1f / (distanceFeet + 1f)) * (1f - hpPercent) * 10f;
                threats.Add((p.CharacterId, threat));
            }

            if (threats.Count == 0) return;

            var primary = threats.OrderByDescending(t => t.ThreatScore).First();
            await _blackboard.SetFact(
                monsterId,
                "PrimaryThreatId",
                primary.CharacterId,
                FactType.Relationship,
                expiration: TimeSpan.FromSeconds(15));
            _logger.LogDebug("Монстр {MonsterId} выбрал основную угрозу: {ThreatId}", monsterId, primary.CharacterId);
        }

        // --------------------------------------------------------------------------------
        // Достижение целей
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Пытается выполнить текущую цель монстра.
        /// </summary>
        private Task<MonsterDecision?> PursueGoal(Guid monsterId, BlackboardGoal goal, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            switch (goal.GoalType)
            {
                case "MoveToLocation":
                    if (goal.Parameters.TryGetValue("X", out var xObj) &&
                        goal.Parameters.TryGetValue("Y", out var yObj) &&
                        xObj is int x && yObj is int y)
                    {
                        return Task.FromResult<MonsterDecision?>(MonsterDecision.MoveTo(x, y));
                    }
                    _logger.LogWarning("Некорректные параметры цели MoveToLocation для {MonsterId}", monsterId);
                    break;

                case "AttackTarget":
                    if (goal.Parameters.TryGetValue("TargetId", out var targetIdObj) &&
                        targetIdObj is Guid targetId)
                    {
                        return Task.FromResult<MonsterDecision?>(MonsterDecision.Attack(targetId));
                    }
                    _logger.LogWarning("Некорректные параметры цели AttackTarget для {MonsterId}", monsterId);
                    break;

                default:
                    _logger.LogDebug("Монстр {MonsterId} не знает, как достичь цели {GoalType}", monsterId, goal.GoalType);
                    break;
            }

            return Task.FromResult<MonsterDecision?>(null);
        }

        // --------------------------------------------------------------------------------
        // Боевое поведение
        // --------------------------------------------------------------------------------

        private async Task<MonsterDecision> DecideCombatAction(
            Guid monsterId,
            CharacterDto monster,
            CombatStatusDto combat,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // Проверяем, может ли монстр действовать вообще
            if (!CanAct(monster, combat, monsterId))
                return MonsterDecision.DoNothing("Монстр не может действовать (оглушён, парализован и т.п.).");

            // Определяем цель
            Guid targetId = await GetPrimaryThreatId(monsterId);
            if (targetId == Guid.Empty)
            {
                // Если основной угрозы нет, ищем любого врага
                foreach (var p in combat.Participants)
                {
                    if (p.CharacterId != monsterId && await IsEnemy(monsterId, p.CharacterId))
                    {
                        targetId = p.CharacterId;
                        break;
                    }
                }
            }

            if (targetId == Guid.Empty)
                return MonsterDecision.DoNothing("Врагов не обнаружено.");

            // 1. Проверяем здоровье: при критическом уровне и возможности убежать — бежим
            float healthPercent = monster.MaxHitPoints > 0 ? (float)monster.HitPoints / monster.MaxHitPoints : 0f;
            if (healthPercent < CriticalHealthThreshold && CanFlee(monster, combat))
            {
                _logger.LogDebug("Монстр {MonsterId} убегает (здоровье критично: {Health:P0})", monsterId, healthPercent);
                return MonsterDecision.Flee();
            }

            // 2. Если есть основное действие — атакуем (вблизи или на расстоянии)
            var participant = combat.Participants.FirstOrDefault(p => p.CharacterId == monsterId);
            bool hasAction = participant?.HasAction ?? false;

            if (hasAction)
            {
                bool inMelee = await IsInMeleeRange(monsterId, targetId);
                if (inMelee)
                {
                    return MonsterDecision.Attack(targetId);
                }
                else
                {
                    // Если нет оружия дальнего боя, но есть возможность подойти — движение
                    return MonsterDecision.RangedAttack(targetId); // предполагаем наличие дальнобойной атаки
                }
            }

            // 3. Если атаковать не можем и не в ближнем бою — двигаемся к цели
            if (!await IsInMeleeRange(monsterId, targetId) && CanMove(monsterId, combat))
            {
                return MonsterDecision.MoveTowards(targetId);
            }

            // 4. В остальных случаях — ждём
            return MonsterDecision.Wait();
        }

        // --------------------------------------------------------------------------------
        // Поведение вне боя
        // --------------------------------------------------------------------------------

        private async Task<MonsterDecision> DecideOutOfCombatAction(
            Guid monsterId,
            CharacterDto monster,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Проверяем, есть ли у монстра активная точка патрулирования
            var patrolXFact = await _blackboard.GetFact(monsterId, "PatrolTargetX");
            var patrolYFact = await _blackboard.GetFact(monsterId, "PatrolTargetY");

            if (patrolXFact?.Value is int targetX && patrolYFact?.Value is int targetY)
            {
                var monsterPos = new Position(monster.PositionX, monster.PositionY);
                var targetPos = new Position(targetX, targetY);
                int distance = monsterPos.ChebyshevDistanceInSquares(targetPos);

                // Если монстр уже у цели (в соседнем квадрате или ближе), выбираем новую точку
                if (distance <= 1)
                {
                    await _blackboard.RemoveFact(monsterId, "PatrolTargetX");
                    await _blackboard.RemoveFact(monsterId, "PatrolTargetY");
                    return await PickNewPatrolPoint(monsterId, monster, ct);
                }

                _logger.LogDebug("Монстр {MonsterId} патрулирует: движение к ({X}, {Y})", monsterId, targetX, targetY);
                return MonsterDecision.MoveTo(targetX, targetY);
            }

            // Если точки нет, выбираем новую
            return await PickNewPatrolPoint(monsterId, monster, ct);
        }

        /// <summary>
        /// Выбирает случайную точку патрулирования в окрестности текущей позиции монстра
        /// и сохраняет её в доске объявлений.
        /// </summary>
        private async Task<MonsterDecision> PickNewPatrolPoint(
            Guid monsterId,
            CharacterDto monster,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Радиус патрулирования: 8 квадратов (40 футов) по обеим осям
            const int patrolRadiusSquares = 8;
            var random = Random.Shared;

            int offsetX = random.Next(-patrolRadiusSquares, patrolRadiusSquares + 1);
            int offsetY = random.Next(-patrolRadiusSquares, patrolRadiusSquares + 1);

            int newX = monster.PositionX + offsetX;
            int newY = monster.PositionY + offsetY;

            await _blackboard.SetFact(monsterId, "PatrolTargetX", newX, FactType.Location, expiration: TimeSpan.FromMinutes(5));
            await _blackboard.SetFact(monsterId, "PatrolTargetY", newY, FactType.Location, expiration: TimeSpan.FromMinutes(5));

            _logger.LogDebug("Монстр {MonsterId} получил новую точку патрулирования: ({X}, {Y})", monsterId, newX, newY);
            return MonsterDecision.MoveTo(newX, newY);
        }

        // --------------------------------------------------------------------------------
        // Вспомогательные проверки
        // --------------------------------------------------------------------------------

        private static bool CanAct(CharacterDto monster, CombatStatusDto combat, Guid monsterId)
        {
            var participant = combat.Participants.FirstOrDefault(p => p.CharacterId == monsterId);
            if (participant == null) return false;

            bool isIncapacitated = monster.Conditions?.Any(c =>
                c is "Stunned" or "Paralyzed" or "Unconscious" or "Incapacitated" or "Petrified") ?? false;

            return !isIncapacitated &&
                   (participant.HasAction || participant.HasBonusAction || participant.HasMovement || participant.HasReaction);
        }

        private static bool CanMove(Guid monsterId, CombatStatusDto combat)
        {
            var participant = combat.Participants.FirstOrDefault(p => p.CharacterId == monsterId);
            return participant != null && participant.HasMovement && participant.MovementRemaining > 0;
        }

        private static bool CanFlee(CharacterDto monster, CombatStatusDto combat)
        {
            bool isRestrainedOrGrappled = monster.Conditions?.Any(c => c is "Restrained" or "Grappled") ?? false;
            if (isRestrainedOrGrappled) return false;

            var participant = combat.Participants.FirstOrDefault(p => p.CharacterId == monster.Id);
            return participant != null && (participant.HasMovement || participant.HasAction);
        }

        private async Task<bool> IsEnemy(Guid monsterId, Guid otherId)
        {
            var fact = await _blackboard.GetFact(monsterId, $"Target_{otherId}_Relation");
            if (fact?.Value?.ToString() == "Enemy")
                return true;
            if (fact?.Value?.ToString() == "Ally")
                return false;

            // Если факт отсутствует, определяем по NPC-статусу
            bool monsterIsNpc = await _ownershipRepository.IsNonPlayerCharacterAsync(monsterId);
            bool otherIsNpc = await _ownershipRepository.IsNonPlayerCharacterAsync(otherId);
            return monsterIsNpc ? !otherIsNpc : otherIsNpc;
        }

        private async Task<bool> IsInMeleeRange(Guid monsterId, Guid targetId)
        {
            var distanceFact = await _blackboard.GetFact(monsterId, $"Target_{targetId}_Distance");
            if (distanceFact?.Value is int distanceFeet)
                return distanceFeet <= MeleeRangeFeet;

            // Если факт отсутствует (устарел или не был обновлён), получаем позиции из проекции и вычисляем
            var monster = await _characterProjection.GetById(monsterId);
            var target = await _characterProjection.GetById(targetId);
            if (monster == null || target == null)
                return false;

            var monsterPos = new Position(monster.PositionX, monster.PositionY);
            var targetPos = new Position(target.PositionX, target.PositionY);
            int dist = monsterPos.ChebyshevDistanceInFeet(targetPos);
            return dist <= MeleeRangeFeet;
        }

        private async Task<int> GetEnemyHitPoints(Guid monsterId, Guid targetId)
        {
            var fact = await _blackboard.GetFact(monsterId, $"Target_{targetId}_HP");
            return fact?.Value is int hp ? hp : 0;
        }

        private async Task<int> GetEnemyMaxHitPoints(Guid monsterId, Guid targetId)
        {
            var fact = await _blackboard.GetFact(monsterId, $"Target_{targetId}_MaxHP");
            return fact?.Value is int maxHp ? maxHp : 0;
        }

        private async Task<Guid> GetPrimaryThreatId(Guid monsterId)
        {
            var fact = await _blackboard.GetFact(monsterId, "PrimaryThreatId");
            return fact?.Value is Guid id ? id : Guid.Empty;
        }
    }

    /// <summary>
    /// Решение, принятое ИИ монстра.
    /// </summary>
    public class MonsterDecision
    {
        /// <summary>Действие (attack, ranged_attack, move_towards, move_to, flee, wait, nothing).</summary>
        public string Action { get; }

        /// <summary>Идентификатор цели (если применимо).</summary>
        public Guid? TargetId { get; }

        /// <summary>Дополнительные параметры (например, координаты для move_to).</summary>
        public object? Parameters { get; }

        /// <summary>Причина решения (для отладки).</summary>
        public string Reason { get; }

        private MonsterDecision(string action, Guid? targetId = null, object? parameters = null, string reason = "")
        {
            Action = action;
            TargetId = targetId;
            Parameters = parameters;
            Reason = reason;
        }

        /// <summary>Атака в ближнем бою.</summary>
        public static MonsterDecision Attack(Guid targetId) =>
            new("attack", targetId, reason: "Атака в ближнем бою");

        /// <summary>Дальнобойная атака.</summary>
        public static MonsterDecision RangedAttack(Guid targetId) =>
            new("ranged_attack", targetId, reason: "Дальнобойная атака");

        /// <summary>Движение в сторону цели.</summary>
        public static MonsterDecision MoveTowards(Guid targetId) =>
            new("move_towards", targetId, reason: "Движение к цели");

        /// <summary>Перемещение в конкретную точку карты.</summary>
        public static MonsterDecision MoveTo(int x, int y) =>
            new("move_to", parameters: new Position(x, y), reason: "Перемещение в точку");

        /// <summary>Бегство с поля боя.</summary>
        public static MonsterDecision Flee() =>
            new("flee", reason: "Бегство");

        /// <summary>Ожидание (пропуск хода).</summary>
        public static MonsterDecision Wait() =>
            new("wait", reason: "Ожидание");

        /// <summary>Ничего не делать (недееспособен или нет доступных действий).</summary>
        public static MonsterDecision DoNothing(string reason) =>
            new("nothing", reason: reason);
    }
}