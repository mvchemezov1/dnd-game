using dnd_game.application.projections;
using dnd_game.domain.commands;
using dnd_game.domain.value_objects;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.services
{
    /// <summary>
    /// Темп путешествия.
    /// </summary>
    public enum TravelPace
    {
        Slow,    // возможность скрытности, -5 к пассивной Внимательности для обнаружения угроз
        Normal,  // стандартное перемещение
        Fast     // штраф -5 к пассивной Внимательности, нельзя скрытно
    }

    /// <summary>
    /// Тип местности.
    /// </summary>
    public enum TerrainType
    {
        Road,
        Plain,
        Forest,
        Hill,
        Mountain,
        Swamp,
        Desert,
        Tundra,
        Water,
        Air
    }

    /// <summary>
    /// Сервис управления путешествиями и перемещениями по глобальной и тактической карте.
    /// Является обёрткой над шиной команд, содержит проверки входных данных и логирование.
    /// </summary>
    public class TravelService(
        ICommandBus commandBus,
        CharacterProjection characterProjection,
        ILogger<TravelService>? logger = null)
    {
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly CharacterProjection _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
        private readonly ILogger<TravelService> _logger = logger ?? NullLogger<TravelService>.Instance;

        /// <summary>
        /// Проверяет, что идентификатор не пустой.
        /// </summary>
        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не должен быть пустым: {paramName}", paramName);
        }

        /// <summary>
        /// Проверяет, что строка не пустая и не состоит из пробелов.
        /// </summary>
        private static void ValidateString(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Строка не должна быть пустой: {paramName}", paramName);
        }

        /// <summary>
        /// Проверяет, что число неотрицательное.
        /// </summary>
        private static void ValidateNonNegative(int value, string paramName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, "Значение не должно быть отрицательным.");
        }

        // ==================== Локальное перемещение ====================

        /// <summary>
        /// Перемещает персонажа на тактической карте (в футах).
        /// </summary>
        /// <summary>
        /// Перемещает персонажа на тактической карте с проверкой доступной дистанции.
        /// </summary>
        public async Task MoveCharacterAsync(
            Guid characterId,
            int targetX,
            int targetY,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            // Получаем текущего персонажа для проверки позиции и скорости
            var character = await _characterProjection.GetById(characterId, cancellationToken);
            if (character == null)
                throw new InvalidOperationException("Персонаж не найден.");

            // Текущая позиция персонажа (клетки)
            var currentPosition = new Position(character.PositionX, character.PositionY);
            var targetPosition = new Position(targetX, targetY);

            // Вычисляем расстояние в футах (Chebyshev, стандартная клетка = 5 футов)
            int distanceFeet = currentPosition.ChebyshevDistanceInFeet(targetPosition);

            // Проверяем, что персонажу хватает скорости
            if (distanceFeet > character.Speed)
            {
                throw new InvalidOperationException(
                    $"Недостаточно скорости: требуется {distanceFeet} фт, доступно {character.Speed} фт.");
            }

            // Отправляем команду перемещения
            await _commandBus.SendAsync(new MoveCharacterToPosition(characterId, targetX, targetY, "Walk"));
            _logger.LogDebug("Персонаж {CharacterId} перемещён в ({X}, {Y})", characterId, targetX, targetY);
        }

        /// <summary>
        /// Использует действие Dash (удвоение скорости на текущий ход).
        /// </summary>
        public async Task DashAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new MoveCharacterWithDash(characterId));
            _logger.LogDebug("Персонаж {CharacterId} использует Dash", characterId);
        }

        /// <summary>
        /// Перемещает персонажа специальным способом (Climb, Swim, Fly, Burrow).
        /// </summary>
        public async Task SpecialMovementAsync(
            Guid characterId,
            int distanceFeet,
            string movementType,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateNonNegative(distanceFeet, nameof(distanceFeet));
            ValidateString(movementType, nameof(movementType));
            cancellationToken.ThrowIfCancellationRequested();

            switch (movementType)
            {
                case "Climb":
                    await _commandBus.SendAsync(new ClimbCharacter(characterId, distanceFeet, 0));
                    break;
                case "Swim":
                    await _commandBus.SendAsync(new SwimCharacter(characterId, distanceFeet, 0));
                    break;
                case "Fly":
                    await _commandBus.SendAsync(new FlyCharacter(characterId, distanceFeet, 0));
                    break;
                case "Burrow":
                    await _commandBus.SendAsync(new BurrowCharacter(characterId, distanceFeet, 0));
                    break;
                default:
                    throw new ArgumentException($"Неизвестный тип перемещения: {movementType}", nameof(movementType));
            }
            _logger.LogDebug("Персонаж {CharacterId} перемещён ({MovementType}) на {Distance} футов", characterId, movementType, distanceFeet);
        }

        /// <summary>
        /// Персонаж пытается скрыться (Hide).
        /// </summary>
        public async Task HideAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new MoveCharacterStealthily(characterId));
            _logger.LogDebug("Персонаж {CharacterId} скрывается", characterId);
        }

        /// <summary>
        /// Персонаж выполняет прыжок.
        /// </summary>
        public async Task JumpAsync(
            Guid characterId,
            string jumpType,
            int strengthScore,
            bool runningStart,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateString(jumpType, nameof(jumpType));
            ValidateNonNegative(strengthScore, nameof(strengthScore));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new JumpCharacter(characterId, jumpType, strengthScore, runningStart));
            _logger.LogDebug("Персонаж {CharacterId} прыгает ({JumpType})", characterId, jumpType);
        }

        // ==================== Глобальное путешествие ====================

        /// <summary>
        /// Начинает путешествие группы по глобальной карте.
        /// </summary>
        public async Task StartJourneyAsync(
            Guid partyId,
            Guid routeId,
            TravelPace pace,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(routeId, nameof(routeId));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new StartJourneyCommand(partyId, routeId, pace.ToString()));
            _logger.LogInformation("Группа {PartyId} начала путешествие по маршруту {RouteId} с темпом {Pace}", partyId, routeId, pace);
        }

        /// <summary>
        /// Завершает путешествие.
        /// </summary>
        public async Task EndJourneyAsync(Guid partyId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new EndJourneyCommand(partyId));
            _logger.LogInformation("Группа {PartyId} завершила путешествие", partyId);
        }

        /// <summary>
        /// Проходит один день пути.
        /// </summary>
        public async Task TravelDayAsync(
            Guid partyId,
            TerrainType terrain,
            int hoursTraveled,
            int navigationCheckResult = 10,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateNonNegative(hoursTraveled, nameof(hoursTraveled));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new TravelDayCommand(partyId, terrain.ToString(), hoursTraveled, navigationCheckResult));
            _logger.LogDebug("Группа {PartyId} путешествует день: {Terrain}, {Hours} ч, навигация {Nav}", partyId, terrain, hoursTraveled, navigationCheckResult);
        }

        /// <summary>
        /// Устанавливает темп путешествия.
        /// </summary>
        public async Task SetPaceAsync(Guid partyId, TravelPace pace, CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new SetTravelPaceCommand(partyId, pace.ToString()));
            _logger.LogDebug("Группа {PartyId} установила темп {Pace}", partyId, pace);
        }

        /// <summary>
        /// Совершает марш-бросок (путешествие сверх 8 часов, грозит истощением).
        /// </summary>
        public async Task ForcedMarchAsync(Guid partyId, int additionalHours, CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateNonNegative(additionalHours, nameof(additionalHours));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new ForcedMarchCommand(partyId, additionalHours));
            _logger.LogDebug("Группа {PartyId} совершает форсированный марш на {Hours} ч", partyId, additionalHours);
        }

        /// <summary>
        /// Выполняет проверку навигации.
        /// </summary>
        public async Task NavigateAsync(
            Guid partyId,
            int survivalCheckRoll,
            int wisdomModifier,
            bool isProficient,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new NavigationCheckCommand(partyId, survivalCheckRoll, wisdomModifier, isProficient));
            _logger.LogDebug("Группа {PartyId} выполняет проверку навигации: бросок {Roll}, мудрость {Wis}", partyId, survivalCheckRoll, wisdomModifier);
        }

        /// <summary>
        /// Группа теряется.
        /// </summary>
        public async Task BecomeLostAsync(Guid partyId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new PartyLostCommand(partyId));
            _logger.LogWarning("Группа {PartyId} потерялась", partyId);
        }

        /// <summary>
        /// Потребляет провизию и воду за указанное количество дней.
        /// </summary>
        public async Task ConsumeResourcesAsync(Guid partyId, int days, CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateNonNegative(days, nameof(days));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new ConsumeResourcesCommand(partyId, days));
            _logger.LogDebug("Группа {PartyId} потребляет ресурсы на {Days} дн.", partyId, days);
        }

        /// <summary>
        /// Инициирует проверку случайной встречи.
        /// </summary>
        public async Task CheckRandomEncounterAsync(Guid partyId, TerrainType terrain, CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new RandomEncounterCheckCommand(partyId, terrain.ToString()));
            _logger.LogDebug("Группа {PartyId} проверяет случайную встречу в местности {Terrain}", partyId, terrain);
        }

        /// <summary>
        /// Применяет усталость членам группы.
        /// </summary>
        public async Task ApplyExhaustionAsync(Guid partyId, int exhaustionLevel, CancellationToken cancellationToken = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateNonNegative(exhaustionLevel, nameof(exhaustionLevel));
            cancellationToken.ThrowIfCancellationRequested();

            await _commandBus.SendAsync(new ApplyExhaustionCommand(partyId, exhaustionLevel));
            _logger.LogDebug("Группа {PartyId} получает истощение уровня {Level}", partyId, exhaustionLevel);
        }

        /// <summary>
        /// Получает базовую скорость персонажа в футах.
        /// </summary>
        public async Task<int> GetCharacterSpeedAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            var character = await _characterProjection.GetById(characterId, cancellationToken);
            return character?.Speed ?? 30;
        }
    }
}