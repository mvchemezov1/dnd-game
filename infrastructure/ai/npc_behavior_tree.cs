#nullable enable
using dnd_game.application.projections;
using dnd_game.domain.commands;
using dnd_game.domain.value_objects;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.ai
{
    // ===================================================================================
    // Статус выполнения узла дерева поведения
    // ===================================================================================

    /// <summary>Результат выполнения узла дерева поведения.</summary>
    public enum BehaviorStatus
    {
        Success,
        Failure,
        Running
    }

    // ===================================================================================
    // Контекст выполнения дерева поведения
    // ===================================================================================

    /// <summary>
    /// Контекст, передаваемый узлам дерева поведения. Содержит ссылки на проекции,
    /// шину команд, доску объявлений и идентификатор NPC, для которого выполняется дерево.
    /// </summary>
    public class BehaviorTreeContext
    {
        public Guid NpcId { get; }
        public IBlackboardStore Blackboard { get; }
        public CharacterProjection CharacterProjection { get; }
        public CombatProjection CombatProjection { get; }
        public CampaignProjection CampaignProjection { get; }
        public ICommandBus CommandBus { get; }

        /// <summary>Локальный кэш данных NPC, обновляется перед каждым тиком.</summary>
        public CharacterDto? SelfCharacter { get; set; }

        /// <summary>Текущий активный бой, если NPC в нём участвует.</summary>
        public CombatStatusDto? ActiveCombat { get; set; }

        public BehaviorTreeContext(
            Guid npcId,
            IBlackboardStore blackboard,
            CharacterProjection characterProjection,
            CombatProjection combatProjection,
            CampaignProjection campaignProjection,
            ICommandBus commandBus)
        {
            if (npcId == Guid.Empty)
                throw new ArgumentException("Идентификатор NPC не может быть пустым.", nameof(npcId));

            NpcId = npcId;
            Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            CharacterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            CombatProjection = combatProjection ?? throw new ArgumentNullException(nameof(combatProjection));
            CampaignProjection = campaignProjection ?? throw new ArgumentNullException(nameof(campaignProjection));
            CommandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        }
    }

    // ===================================================================================
    // Абстрактный узел дерева поведения
    // ===================================================================================

    /// <summary>Базовый класс для всех узлов дерева поведения.</summary>
    public abstract class BehaviorTreeNode
    {
        /// <summary>Выполняет узел и возвращает статус.</summary>
        public abstract Task<BehaviorStatus> Execute(BehaviorTreeContext context);
    }

    // ===================================================================================
    // Композитные узлы
    // ===================================================================================

    /// <summary>
    /// Последовательность: выполняет дочерние узлы по порядку, пока все не вернут Success.
    /// При первом Failure или Running возвращает соответствующий статус.
    /// </summary>
    public class SequenceNode : BehaviorTreeNode
    {
        private readonly List<BehaviorTreeNode> _children;

        public SequenceNode(IEnumerable<BehaviorTreeNode> children)
        {
            _children = children?.ToList() ?? throw new ArgumentNullException(nameof(children));
            if (_children.Count == 0)
                throw new ArgumentException("Последовательность должна содержать хотя бы один узел.", nameof(children));
        }

        public override async Task<BehaviorStatus> Execute(BehaviorTreeContext context)
        {
            foreach (var child in _children)
            {
                var status = await child.Execute(context);
                if (status != BehaviorStatus.Success)
                    return status;
            }
            return BehaviorStatus.Success;
        }
    }

    /// <summary>
    /// Селектор: выполняет дочерние узлы по порядку, пока один не вернёт Success.
    /// При Running запоминает активного ребёнка для следующего тика.
    /// </summary>
    public class SelectorNode : BehaviorTreeNode
    {
        private readonly List<BehaviorTreeNode> _children;
        private int _runningIndex = -1;

        public SelectorNode(IEnumerable<BehaviorTreeNode> children)
        {
            _children = children?.ToList() ?? throw new ArgumentNullException(nameof(children));
            if (_children.Count == 0)
                throw new ArgumentException("Селектор должен содержать хотя бы один узел.", nameof(children));
        }

        public override async Task<BehaviorStatus> Execute(BehaviorTreeContext context)
        {
            int start = _runningIndex >= 0 ? _runningIndex : 0;
            for (int i = start; i < _children.Count; i++)
            {
                var status = await _children[i].Execute(context);
                if (status == BehaviorStatus.Success)
                {
                    _runningIndex = -1;
                    return BehaviorStatus.Success;
                }
                if (status == BehaviorStatus.Running)
                {
                    _runningIndex = i;
                    return BehaviorStatus.Running;
                }
            }
            _runningIndex = -1;
            return BehaviorStatus.Failure;
        }
    }

    /// <summary>
    /// Параллельный узел: запускает всех детей одновременно и возвращает Success,
    /// если заданное количество детей завершилось успехом. При наличии Running возвращает Running.
    /// </summary>
    public class ParallelNode : BehaviorTreeNode
    {
        private readonly List<BehaviorTreeNode> _children;
        private readonly int _requiredSuccesses;
        private readonly ILogger<ParallelNode>? _logger;

        public ParallelNode(
            IEnumerable<BehaviorTreeNode> children,
            int requiredSuccesses,
            ILogger<ParallelNode>? logger = null)
        {
            _children = children?.ToList() ?? throw new ArgumentNullException(nameof(children));
            if (_children.Count == 0)
                throw new ArgumentException("Параллельный узел должен содержать хотя бы один дочерний узел.", nameof(children));
            if (requiredSuccesses <= 0 || requiredSuccesses > _children.Count)
                throw new ArgumentOutOfRangeException(nameof(requiredSuccesses),
                    "Количество требуемых успехов должно быть от 1 до количества дочерних узлов.");
            _requiredSuccesses = requiredSuccesses;
            _logger = logger;
        }

        public override async Task<BehaviorStatus> Execute(BehaviorTreeContext context)
        {
            var tasks = _children.Select(async child =>
            {
                try
                {
                    return await child.Execute(context);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Ошибка выполнения дочернего узла {ChildType} в ParallelNode", child.GetType().Name);
                    return BehaviorStatus.Failure;
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks);

            int successes = results.Count(r => r == BehaviorStatus.Success);
            int running = results.Count(r => r == BehaviorStatus.Running);

            if (running > 0)
                return BehaviorStatus.Running;

            return successes >= _requiredSuccesses ? BehaviorStatus.Success : BehaviorStatus.Failure;
        }
    }

    // ===================================================================================
    // Декораторы
    // ===================================================================================

    /// <summary>Инвертирует результат дочернего узла (Success ↔ Failure).</summary>
    public class InverterNode(BehaviorTreeNode child) : BehaviorTreeNode
    {
        private readonly BehaviorTreeNode _child = child ?? throw new ArgumentNullException(nameof(child));

        public override async Task<BehaviorStatus> Execute(BehaviorTreeContext context)
        {
            var status = await _child.Execute(context);
            return status switch
            {
                BehaviorStatus.Success => BehaviorStatus.Failure,
                BehaviorStatus.Failure => BehaviorStatus.Success,
                _ => status
            };
        }
    }

    /// <summary>
    /// Повторяет выполнение дочернего узла заданное количество раз.
    /// Возвращает Running, пока повторения не завершены; Success после достижения лимита.
    /// </summary>
    public class RepeaterNode : BehaviorTreeNode
    {
        private readonly BehaviorTreeNode _child;
        private readonly int _maxRepeats;
        private int _count;

        public RepeaterNode(BehaviorTreeNode child, int maxRepeats = -1)
        {
            _child = child ?? throw new ArgumentNullException(nameof(child));
            if (maxRepeats < -1 || maxRepeats == 0)
                throw new ArgumentOutOfRangeException(nameof(maxRepeats), "Количество повторов должно быть положительным или -1 для бесконечного повторения.");
            _maxRepeats = maxRepeats;
        }

        public override async Task<BehaviorStatus> Execute(BehaviorTreeContext context)
        {
            if (_maxRepeats >= 0 && _count >= _maxRepeats)
            {
                _count = 0;
                return BehaviorStatus.Success;
            }

            var status = await _child.Execute(context);

            if (status == BehaviorStatus.Failure)
            {
                _count = 0;
                return BehaviorStatus.Failure;
            }

            if (status == BehaviorStatus.Running)
                return BehaviorStatus.Running;

            // status == Success
            _count++;

            if (_maxRepeats >= 0 && _count >= _maxRepeats)
            {
                _count = 0;
                return BehaviorStatus.Success;
            }

            return BehaviorStatus.Running; // ждём следующего тика
        }
    }

    /// <summary>
    /// Повторяет выполнение дочернего узла, пока тот не вернёт Success.
    /// При Failure возвращает Running, при Success — Success.
    /// </summary>
    public class UntilSuccessNode(BehaviorTreeNode child) : BehaviorTreeNode
    {
        private readonly BehaviorTreeNode _child = child ?? throw new ArgumentNullException(nameof(child));

        public override async Task<BehaviorStatus> Execute(BehaviorTreeContext context)
        {
            var status = await _child.Execute(context);
            return status == BehaviorStatus.Failure ? BehaviorStatus.Running : status;
        }
    }

    // ===================================================================================
    // Условия (листья, возвращают Success/Failure)
    // ===================================================================================

    /// <summary>Условие, проверяемое через асинхронную функцию.</summary>
    public class ConditionNode(Func<BehaviorTreeContext, Task<bool>> condition) : BehaviorTreeNode
    {
        private readonly Func<BehaviorTreeContext, Task<bool>> _condition = condition ?? throw new ArgumentNullException(nameof(condition));

        public override async Task<BehaviorStatus> Execute(BehaviorTreeContext context)
        {
            return await _condition(context) ? BehaviorStatus.Success : BehaviorStatus.Failure;
        }
    }

    /// <summary>Фабрики стандартных условий для DnD.</summary>
    public static class BehaviorTreeConditions
    {
        public static ConditionNode IsAlive() =>
            new(async ctx =>
            {
                var character = ctx.SelfCharacter ?? await ctx.CharacterProjection.GetById(ctx.NpcId);
                return character != null && !character.IsDead && character.HitPoints > 0;
            });

        public static ConditionNode HealthAbovePercent(float percent)
        {
            if (percent < 0 || percent > 1)
                throw new ArgumentOutOfRangeException(nameof(percent), "Процент здоровья должен быть от 0 до 1.");
            return new ConditionNode(async ctx =>
            {
                var character = ctx.SelfCharacter ?? await ctx.CharacterProjection.GetById(ctx.NpcId);
                if (character == null || character.MaxHitPoints == 0) return false;
                return (float)character.HitPoints / character.MaxHitPoints >= percent;
            });
        }

        public static ConditionNode HasEnemyInCombat() =>
            new(async ctx =>
            {
                if (ctx.ActiveCombat == null) return false;
                foreach (var participant in ctx.ActiveCombat.Participants)
                {
                    if (participant.CharacterId == ctx.NpcId) continue;
                    var fact = await ctx.Blackboard.GetFact(ctx.NpcId, $"Target_{participant.CharacterId}_Relation");
                    if (fact?.Value?.ToString() == "Enemy") return true;
                }
                return false;
            });

        public static ConditionNode IsInCombat() =>
            new(ctx => Task.FromResult(ctx.ActiveCombat != null && ctx.ActiveCombat.IsActive));

        public static ConditionNode IsMyTurn() =>
            new(ctx =>
            {
                if (ctx.ActiveCombat == null) return Task.FromResult(false);
                var current = ctx.ActiveCombat.Participants.FirstOrDefault(p => p.IsCurrentTurn);
                return Task.FromResult(current?.CharacterId == ctx.NpcId);
            });

        public static ConditionNode IsWithinMeleeRange(Guid targetId) =>
            new(async ctx =>
            {
                var distanceFact = await ctx.Blackboard.GetFact(ctx.NpcId, $"Target_{targetId}_Distance");
                return distanceFact?.Value is int distance && distance <= 5;
            });

        public static ConditionNode HasItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
            return new ConditionNode(async ctx =>
            {
                var character = ctx.SelfCharacter ?? await ctx.CharacterProjection.GetById(ctx.NpcId);
                return character?.Inventory?.Any(i => i.ItemId == itemId) ?? false;
            });
        }

        public static ConditionNode IsDaytime() =>
            new(async ctx =>
            {
                var campaignFact = await ctx.Blackboard.GetFact(ctx.NpcId, "CampaignId");
                if (campaignFact?.Value is not Guid campaignId) return true;
                var state = await ctx.CampaignProjection.GetCampaignState(campaignId);
                return state == null || (state.Hour >= 6 && state.Hour < 18);
            });
    }

    // ===================================================================================
    // Действия (листья, выполняют команду и возвращают Success/Running/Failure)
    // ===================================================================================

    /// <summary>Действие, выполняемое через асинхронную функцию.</summary>
    public class ActionNode(Func<BehaviorTreeContext, Task<BehaviorStatus>> action) : BehaviorTreeNode
    {
        private readonly Func<BehaviorTreeContext, Task<BehaviorStatus>> _action = action ?? throw new ArgumentNullException(nameof(action));

        public override async Task<BehaviorStatus> Execute(BehaviorTreeContext context)
        {
            return await _action(context);
        }
    }

    /// <summary>Фабрики стандартных действий DnD.</summary>
    public static class BehaviorTreeActions
    {
        public static ActionNode Attack(Guid targetId) =>
            new(async ctx =>
            {
                if (ctx.ActiveCombat == null)
                    return BehaviorStatus.Failure;

                await ctx.CommandBus.SendAsync(new TakeStandardAction(
                    ctx.ActiveCombat.CombatId,
                    ctx.NpcId,
                    "Attack",
                    targetId));
                return BehaviorStatus.Success;
            });

        public static ActionNode MoveToTarget(Guid targetId) =>
            new(async ctx =>
            {
                var target = await ctx.CharacterProjection.GetById(targetId);
                if (target == null)
                    return BehaviorStatus.Failure;

                // Перемещаемся к позиции цели (используем MoveCharacterToPosition)
                await ctx.CommandBus.SendAsync(new MoveCharacterToPosition(
                    ctx.NpcId,
                    target.PositionX,
                    target.PositionY,
                    "Walk"));
                return BehaviorStatus.Success;
            });

        public static ActionNode Flee() =>
            new(async ctx =>
            {
                if (ctx.ActiveCombat == null)
                    return BehaviorStatus.Failure;

                // Используем Dash, чтобы увеличить дистанцию бегства
                await ctx.CommandBus.SendAsync(new TakeStandardAction(
                    ctx.ActiveCombat.CombatId,
                    ctx.NpcId,
                    "Dash",
                    null));
                // TODO: также нужно отправить команду перемещения прочь от врага.
                // Здесь можно вычислить направление от ближайшего врага и двигаться в противоположную сторону.
                return BehaviorStatus.Success;
            });

        public static ActionNode Wait() =>
            new(_ => Task.FromResult(BehaviorStatus.Success));

        public static ActionNode UseHealingPotion() =>
            new(async ctx =>
            {
                var character = ctx.SelfCharacter ?? await ctx.CharacterProjection.GetById(ctx.NpcId);
                var potion = character?.Inventory?.FirstOrDefault(i =>
                    i.Name.Contains("Potion of Healing", StringComparison.OrdinalIgnoreCase));
                if (potion != null)
                {
                    // Лечим на среднее значение зелья лечения: 2d4+2 (среднее 7)
                    await ctx.CommandBus.SendAsync(new HealCharacter(ctx.NpcId, 7));
                    await ctx.CommandBus.SendAsync(new RemoveInventoryItem(ctx.NpcId, potion.ItemId, 1));
                    return BehaviorStatus.Success;
                }
                return BehaviorStatus.Failure;
            });

        public static ActionNode Patrol() =>
            new(async ctx =>
            {
                // Проверяем, есть ли точка патрулирования
                var patrolXFact = await ctx.Blackboard.GetFact(ctx.NpcId, "PatrolTargetX");
                var patrolYFact = await ctx.Blackboard.GetFact(ctx.NpcId, "PatrolTargetY");

                if (patrolXFact?.Value is int targetX && patrolYFact?.Value is int targetY)
                {
                    var self = ctx.SelfCharacter ?? await ctx.CharacterProjection.GetById(ctx.NpcId);
                    if (self == null) return BehaviorStatus.Failure;

                    var selfPos = new Position(self.PositionX, self.PositionY);
                    var targetPos = new Position(targetX, targetY);
                    int distSquares = selfPos.ChebyshevDistanceInSquares(targetPos);

                    if (distSquares <= 1)
                    {
                        // Достигли точки — удаляем её (в следующем тике выберем новую)
                        await ctx.Blackboard.RemoveFact(ctx.NpcId, "PatrolTargetX");
                        await ctx.Blackboard.RemoveFact(ctx.NpcId, "PatrolTargetY");
                        return BehaviorStatus.Success;
                    }

                    // Отправляем команду перемещения к точке патрулирования
                    await ctx.CommandBus.SendAsync(new MoveCharacterToPosition(
                        ctx.NpcId, targetX, targetY, "Walk"));
                    return BehaviorStatus.Running; // продолжаем движение
                }

                // Если точки нет — выбираем случайную в радиусе 8 квадратов
                var random = Random.Shared;
                int offsetX = random.Next(-8, 9);
                int offsetY = random.Next(-8, 9);
                var current = ctx.SelfCharacter ?? await ctx.CharacterProjection.GetById(ctx.NpcId);
                if (current == null) return BehaviorStatus.Failure;

                int newX = current.PositionX + offsetX;
                int newY = current.PositionY + offsetY;

                await ctx.Blackboard.SetFact(ctx.NpcId, "PatrolTargetX", newX, FactType.Location, expiration: TimeSpan.FromMinutes(5));
                await ctx.Blackboard.SetFact(ctx.NpcId, "PatrolTargetY", newY, FactType.Location, expiration: TimeSpan.FromMinutes(5));

                await ctx.CommandBus.SendAsync(new MoveCharacterToPosition(ctx.NpcId, newX, newY, "Walk"));
                return BehaviorStatus.Running;
            });
    }

    // ===================================================================================
    // Класс дерева поведения
    // ===================================================================================

    /// <summary>
    /// Дерево поведения NPC. Хранит корневой узел и контекст, обновляет состояние перед тиком.
    /// </summary>
    public class NpcBehaviorTree(BehaviorTreeNode root, BehaviorTreeContext context)
    {
        private readonly BehaviorTreeNode _root = root ?? throw new ArgumentNullException(nameof(root));
        private readonly BehaviorTreeContext _context = context ?? throw new ArgumentNullException(nameof(context));
        private DateTime _lastTick;

        /// <summary>
        /// Выполняет один тик дерева поведения. Обновляет контекстные данные
        /// (персонаж, активный бой) и запускает корневой узел.
        /// </summary>
        public async Task Tick(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshContext(cancellationToken);
            await _root.Execute(_context);
            _lastTick = DateTime.UtcNow;
        }

        private async Task RefreshContext(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _context.SelfCharacter = await _context.CharacterProjection.GetById(_context.NpcId, ct);

            var combatFact = await _context.Blackboard.GetFact(_context.NpcId, "CurrentCombatId");
            if (combatFact?.Value is Guid combatId && combatId != Guid.Empty)
            {
                _context.ActiveCombat = await _context.CombatProjection.GetStatus(combatId, ct);
            }
            else
            {
                _context.ActiveCombat = null;
            }

            // Очистка устаревших фактов раз в 10 секунд
            if ((DateTime.UtcNow - _lastTick).TotalSeconds > 10)
            {
                await _context.Blackboard.ClearExpiredFacts();
            }
        }
    }
}