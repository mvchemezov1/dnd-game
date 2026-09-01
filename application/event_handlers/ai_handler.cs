#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.events;
using dnd_game.infrastructure.ai;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.application.event_handlers
{
    /// <summary>
    /// Обработчик ИИ: при событиях боя запускает принятие решений для NPC.
    /// </summary>
    public class AiHandler : IEventHandler<CharacterDied>,
                             IEventHandler<CombatStarted>,
                             IEventHandler<CombatEnded>
    {
        private readonly MonsterAi _monsterAi;
        private readonly ICommandBus _commandBus;

        public AiHandler(MonsterAi monsterAi, ICommandBus commandBus)
        {
            _monsterAi = monsterAi ?? throw new ArgumentNullException(nameof(monsterAi));
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        }

        public async Task Handle(CharacterDied e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // При смерти персонажа можно обновить цели ИИ, но для простоты ничего не делаем.
            await Task.CompletedTask;
        }

        public async Task Handle(CombatStarted e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // Для каждого NPC-участника (у которого нет владельца-игрока) можно запустить ИИ.
            foreach (var participantId in e.Participants)
            {
                var decision = await _monsterAi.DecideAction(participantId, ct);
                if (decision.Action == "attack" && decision.TargetId.HasValue)
                {
                    await _commandBus.SendAsync(
                        new dnd_game.domain.commands.TakeStandardAction(
                            e.CombatId,
                            participantId,
                            "Attack",
                            decision.TargetId.Value),
                        ct);
                }
                // другие действия можно обработать аналогично
            }
        }

        public async Task Handle(CombatEnded e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // Остановка ИИ — заглушка, так как ИИ работает по требованию.
            await Task.CompletedTask;
        }
    }
}