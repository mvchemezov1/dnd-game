using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.exceptions;
using dnd_game.domain.rules;
using dnd_game.domain.value_objects;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.world;

namespace dnd_game.application.command_handlers
{
    /// <summary>
    /// Базовый класс для обработчиков команд перемещения.
    /// Содержит общую логику загрузки/сохранения персонажа и доступ к сетке мира.
    /// </summary>
    public abstract class MovementCommandHandlerBase(IEventStore eventStore, IGridProvider gridProvider)
    {
        protected readonly IEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        protected readonly IGridProvider _grid = gridProvider ?? throw new ArgumentNullException(nameof(gridProvider));

        /// <summary>
        /// Загружает агрегат персонажа по идентификатору.
        /// Если персонаж не найден, выбрасывает исключение с русским сообщением.
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

        /// <summary>
        /// Проверяет возможность перемещения по сетке и вычисляет стоимость пути.
        /// Возвращает стоимость пути или выбрасывает исключение, если путь невозможен.
        /// </summary>
        protected int ValidateAndCalculatePath(CharacterAggregate character, int targetX, int targetY)
        {
            var currentPos = new Position(character.PositionX, character.PositionY);
            var targetPos = new Position(targetX, targetY);

            if (!_grid.InBounds(targetPos.X, targetPos.Y))
                throw new InvalidAction("Целевая позиция вне границ карты.");

            var targetCell = _grid.GetCell(targetPos.X, targetPos.Y);
            int targetCost = MovementRules.GetMovementCostPerCell(targetCell.Terrain);
            if (targetCost < 0)
                throw new InvalidAction("Целевая клетка непроходима.");

            var path = _grid.FindPath(currentPos, targetPos);
            if (path.Count < 2 || path[^1] != targetPos)
                throw new InvalidAction("Не существует допустимого пути до цели.");

            int pathCost = MovementRules.CalculatePathCost(_grid, path);
            if (pathCost < 0)
                throw new InvalidAction("Путь содержит непроходимые клетки.");

            return pathCost;
        }
    }

    /// <summary>
    /// Обработчик команд, связанных с перемещением персонажей.
    /// Реализует все команды движения, включая специальные виды, прыжки, управление скоростью и препятствия.
    /// </summary>
    public class MovementHandler(IEventStore eventStore, IGridProvider gridProvider) : MovementCommandHandlerBase(eventStore, gridProvider),
                                   ICommandHandler<MoveCharacter>,
                                   ICommandHandler<MoveCharacterToPosition>,
                                   ICommandHandler<MoveCharacterWithDash>,
                                   ICommandHandler<MoveCharacterWithDisengage>,
                                   ICommandHandler<MoveCharacterStealthily>,
                                   ICommandHandler<ClimbCharacter>,
                                   ICommandHandler<SwimCharacter>,
                                   ICommandHandler<FlyCharacter>,
                                   ICommandHandler<BurrowCharacter>,
                                   ICommandHandler<JumpCharacter>,
                                   ICommandHandler<SetCharacterSpeed>,
                                   ICommandHandler<ResetCharacterSpeed>,
                                   ICommandHandler<ApplyDifficultTerrain>,
                                   ICommandHandler<RemoveDifficultTerrain>,
                                   ICommandHandler<ApplyMovementImpairment>,
                                   ICommandHandler<RemoveMovementImpairment>,
                                   ICommandHandler<MakeAthleticsCheckForMovement>,
                                   ICommandHandler<MakeAcrobaticsCheckForMovement>,
                                   ICommandHandler<TakeFallDamage>
    {

        // ---------- Основное перемещение ----------

        public async Task Handle(MoveCharacter command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            int pathCost = ValidateAndCalculatePath(character, command.TargetX, command.TargetY);
            if (pathCost > character.Speed)
                throw new InvalidAction($"Недостаточно движения. Требуется: {pathCost}, доступно: {character.Speed}.");

            character.MoveToPosition(command.TargetX, command.TargetY, "Walk");
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(MoveCharacterToPosition command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            int pathCost = ValidateAndCalculatePath(character, command.TargetX, command.TargetY);
            if (pathCost > character.Speed)
                throw new InvalidAction($"Недостаточно движения. Требуется: {pathCost}, доступно: {character.Speed}.");

            character.MoveToPosition(command.TargetX, command.TargetY, command.MovementType);
            await SaveCharacterAsync(character, cancellationToken);
        }

        // ---------- Специальные действия ----------

        public async Task Handle(MoveCharacterWithDash command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.Dash();
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(MoveCharacterWithDisengage command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.Disengage();
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(MoveCharacterStealthily command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.Hide();
            await SaveCharacterAsync(character, cancellationToken);
        }

        // ---------- Специальные виды движения ----------

        public async Task Handle(ClimbCharacter command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.Climb(command.DistanceFeet, command.ClimbSpeedUsed);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(SwimCharacter command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.Swim(command.DistanceFeet, command.SwimSpeedUsed);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(FlyCharacter command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.Fly(command.DistanceFeet, command.FlySpeedUsed);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(BurrowCharacter command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.Burrow(command.DistanceFeet, command.BurrowSpeedUsed);
            await SaveCharacterAsync(character, cancellationToken);
        }

        // ---------- Прыжки ----------

        public async Task Handle(JumpCharacter command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.Jump(command.JumpType, command.StrengthScore, command.RunningStart);
            await SaveCharacterAsync(character, cancellationToken);
        }

        // ---------- Управление скоростью ----------

        public async Task Handle(SetCharacterSpeed command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.SetTemporarySpeed(command.NewSpeed, command.MovementType);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(ResetCharacterSpeed command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.ResetSpeedToBase();
            await SaveCharacterAsync(character, cancellationToken);
        }

        // ---------- Модификаторы местности ----------

        public async Task Handle(ApplyDifficultTerrain command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.ApplyDifficultTerrain(command.Multiplier);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(RemoveDifficultTerrain command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.RemoveDifficultTerrain();
            await SaveCharacterAsync(character, cancellationToken);
        }

        // ---------- Ограничения движения ----------

        public async Task Handle(ApplyMovementImpairment command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.ApplyMovementImpairment(command.ImpairmentType, command.SpeedReduction);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(RemoveMovementImpairment command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.RemoveMovementImpairment(command.ImpairmentType);
            await SaveCharacterAsync(character, cancellationToken);
        }

        // ---------- Проверки навыков ----------

        public async Task Handle(MakeAthleticsCheckForMovement command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.MakeAthleticsCheck(command.DifficultyClass, command.RollResult,
                                         command.ProficiencyBonus, command.StrengthModifier);
            await SaveCharacterAsync(character, cancellationToken);
        }

        public async Task Handle(MakeAcrobaticsCheckForMovement command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.MakeAcrobaticsCheck(command.DifficultyClass, command.RollResult,
                                          command.ProficiencyBonus, command.DexterityModifier);
            await SaveCharacterAsync(character, cancellationToken);
        }

        // ---------- Падение ----------

        public async Task Handle(TakeFallDamage command, CancellationToken cancellationToken)
        {
            var character = await GetCharacterAsync(command.CharacterId, cancellationToken);
            character.TakeFallDamage(command.FallDistanceFeet);
            await SaveCharacterAsync(character, cancellationToken);
        }
    }
}