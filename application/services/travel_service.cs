#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.projections;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.domain.value_objects;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace dnd_game.application.services
{
    public enum TravelPace { Slow, Normal, Fast }

    public enum TerrainType { Road, Plain, Forest, Hill, Mountain, Swamp, Desert, Tundra, Water, Air }

    public class TravelService
    {
        private readonly ICommandBus _commandBus;
        private readonly CharacterProjection _characterProjection;
        private readonly PermissionChecker _permissionChecker;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<TravelService> _logger;

        public TravelService(
            ICommandBus commandBus,
            CharacterProjection characterProjection,
            PermissionChecker permissionChecker,
            ICurrentUserService currentUserService,
            ILogger<TravelService>? logger = null)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
            _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
            _logger = logger ?? NullLogger<TravelService>.Instance;
        }

        // ---------- Вспомогательные методы ----------
        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty) throw new ArgumentException($"Идентификатор не должен быть пустым: {paramName}", paramName);
        }

        private async Task EnsureCanControlCharacterAsync(Guid characterId, CancellationToken ct)
        {
            if (!await _permissionChecker.CanControlCharacterAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для управления этим персонажем.");
        }

        private async Task EnsureIsGameMasterOfCampaignAsync(Guid campaignId, CancellationToken ct)
        {
            if (!await _permissionChecker.IsGameMasterOfCampaignAsync(campaignId, ct))
                throw new UnauthorizedAccessException("Только Мастер кампании может выполнить это действие.");
        }

        // ==================== Локальное перемещение (персонаж) ====================

        public async Task MoveCharacterAsync(Guid characterId, int targetX, int targetY, CancellationToken ct = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            await EnsureCanControlCharacterAsync(characterId, ct);

            var character = await _characterProjection.GetById(characterId, ct);
            if (character == null) throw new InvalidOperationException("Персонаж не найден.");

            var currentPosition = new Position(character.PositionX, character.PositionY);
            var targetPosition = new Position(targetX, targetY);
            int distanceFeet = currentPosition.ChebyshevDistanceInFeet(targetPosition);

            if (distanceFeet > character.Speed)
                throw new InvalidOperationException($"Недостаточно скорости: требуется {distanceFeet} фт, доступно {character.Speed} фт.");

            await _commandBus.SendAsync(new MoveCharacterToPosition(characterId, targetX, targetY, "Walk"), ct);
        }

        public async Task DashAsync(Guid characterId, CancellationToken ct = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            await EnsureCanControlCharacterAsync(characterId, ct);
            await _commandBus.SendAsync(new MoveCharacterWithDash(characterId), ct);
        }

        public async Task SpecialMovementAsync(Guid characterId, int distanceFeet, string movementType, CancellationToken ct = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            if (distanceFeet < 0) throw new ArgumentOutOfRangeException(nameof(distanceFeet));
            if (string.IsNullOrWhiteSpace(movementType)) throw new ArgumentException("Тип перемещения не может быть пустым.", nameof(movementType));
            await EnsureCanControlCharacterAsync(characterId, ct);

            switch (movementType)
            {
                case "Climb": await _commandBus.SendAsync(new ClimbCharacter(characterId, distanceFeet, 0), ct); break;
                case "Swim": await _commandBus.SendAsync(new SwimCharacter(characterId, distanceFeet, 0), ct); break;
                case "Fly": await _commandBus.SendAsync(new FlyCharacter(characterId, distanceFeet, 0), ct); break;
                case "Burrow": await _commandBus.SendAsync(new BurrowCharacter(characterId, distanceFeet, 0), ct); break;
                default: throw new ArgumentException($"Неизвестный тип перемещения: {movementType}", nameof(movementType));
            }
        }

        public async Task HideAsync(Guid characterId, CancellationToken ct = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            await EnsureCanControlCharacterAsync(characterId, ct);
            await _commandBus.SendAsync(new MoveCharacterStealthily(characterId), ct);
        }

        public async Task JumpAsync(Guid characterId, string jumpType, int strengthScore, bool runningStart, CancellationToken ct = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            if (string.IsNullOrWhiteSpace(jumpType)) throw new ArgumentException("Тип прыжка не может быть пустым.", nameof(jumpType));
            await EnsureCanControlCharacterAsync(characterId, ct);
            await _commandBus.SendAsync(new JumpCharacter(characterId, jumpType, strengthScore, runningStart), ct);
        }

        public async Task<int> GetCharacterSpeedAsync(Guid characterId, CancellationToken ct = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            await EnsureCanControlCharacterAsync(characterId, ct); // или CanView, но для простоты
            var character = await _characterProjection.GetById(characterId, ct);
            return character?.Speed ?? 30;
        }

        // ==================== Глобальное путешествие (группа) ====================

        public async Task StartJourneyAsync(Guid partyId, Guid routeId, TravelPace pace, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(routeId, nameof(routeId));
            ValidateGuid(campaignId, nameof(campaignId));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new StartJourneyCommand(partyId, routeId, pace.ToString()), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task EndJourneyAsync(Guid partyId, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new EndJourneyCommand(partyId), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task TravelDayAsync(Guid partyId, TerrainType terrain, int hoursTraveled, Guid campaignId, int navigationCheckResult = 10, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            if (hoursTraveled < 0) throw new ArgumentOutOfRangeException(nameof(hoursTraveled));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new TravelDayCommand(partyId, terrain.ToString(), hoursTraveled, navigationCheckResult), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task SetPaceAsync(Guid partyId, TravelPace pace, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new SetTravelPaceCommand(partyId, pace.ToString()), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task ForcedMarchAsync(Guid partyId, int additionalHours, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            if (additionalHours < 0) throw new ArgumentOutOfRangeException(nameof(additionalHours));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new ForcedMarchCommand(partyId, additionalHours), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task NavigateAsync(Guid partyId, int survivalCheckRoll, int wisdomModifier, bool isProficient, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new NavigationCheckCommand(partyId, survivalCheckRoll, wisdomModifier, isProficient), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task BecomeLostAsync(Guid partyId, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new PartyLostCommand(partyId), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task ConsumeResourcesAsync(Guid partyId, int days, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            if (days < 0) throw new ArgumentOutOfRangeException(nameof(days));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new ConsumeResourcesCommand(partyId, days), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task CheckRandomEncounterAsync(Guid partyId, TerrainType terrain, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new RandomEncounterCheckCommand(partyId, terrain.ToString()), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }

        public async Task ApplyExhaustionAsync(Guid partyId, int exhaustionLevel, Guid campaignId, CancellationToken ct = default)
        {
            ValidateGuid(partyId, nameof(partyId));
            ValidateGuid(campaignId, nameof(campaignId));
            if (exhaustionLevel < 0) throw new ArgumentOutOfRangeException(nameof(exhaustionLevel));
            await EnsureIsGameMasterOfCampaignAsync(campaignId, ct);
            await _commandBus.SendAsync(new ApplyExhaustionCommand(partyId, exhaustionLevel), new CommandContext
            {
                UserId = _currentUserService.GetCurrentUserId(),
                GameSessionId = campaignId,
                CancellationToken = ct
            });
        }
    }
}