using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;

namespace dnd_game.application.command_handlers
{
    /// <summary>
    /// Базовый класс для обработчиков команд, работающих с персонажем в контексте отдыха.
    /// Инкапсулирует общую логику загрузки и сохранения агрегата.
    /// </summary>
    public abstract class RestCommandHandlerBase(IEventStore eventStore)
    {
        protected readonly IEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));

        /// <summary>
        /// Загружает агрегат персонажа по идентификатору. Если персонаж не найден, выбрасывает исключение с русским сообщением.
        /// </summary>
        protected async Task<CharacterAggregate> GetCharacterAsync(Guid characterId, CancellationToken cancellationToken)
        {
            var character = await _eventStore.Load<CharacterAggregate>(characterId, cancellationToken) ?? throw new InvalidAction("Персонаж не найден");
            return character;
        }

        /// <summary>
        /// Сохраняет изменения агрегата персонажа.
        /// </summary>
        protected async Task SaveCharacterAsync(CharacterAggregate character, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(character);
            await _eventStore.Save(character, cancellationToken);
        }
    }

    /// <summary>
    /// Обработчик команд, связанных с отдыхом персонажа.
    /// Реализует команды начала, завершения, прерывания отдыха и траты костей хитов.
    /// </summary>
    public class RestHandler(IEventStore eventStore) : RestCommandHandlerBase(eventStore),
                               ICommandHandler<StartRest>,
                               ICommandHandler<EndRest>,
                               ICommandHandler<SpendHitDie>,
                               ICommandHandler<InterruptRest>
    {
        public async Task Handle(StartRest command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.StartRest(command.RestType);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(SpendHitDie command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.SpendHitDie(command.HitDieType, command.Roll, command.ConstitutionModifier);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(InterruptRest command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.InterruptRest(command.InterruptionType);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(EndRest command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.EndRest();
            await SaveCharacterAsync(character, cancellationToken);
        }
    }
}