#nullable enable
using dnd_game.application.projections;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.domain.events;      // TradeItem, TradeOfferCreated, TradeOfferAccepted и др.
using dnd_game.infrastructure.config; // Settings и TechnicalLimits
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.services
{
    /// <summary>Статус торгового предложения.</summary>
    public enum TradeOfferStatus
    {
        Pending,
        Accepted,
        Declined,
        Cancelled
    }

    /// <summary>Предложение обмена между персонажами.</summary>
    public class TradeOffer
    {
        public Guid OfferId { get; set; }
        public Guid FromCharacterId { get; set; }
        public Guid ToCharacterId { get; set; }
        public List<TradeItem> OfferedItems { get; set; } = new();
        public int OfferedGold { get; set; }
        public List<TradeItem> RequestedItems { get; set; } = new();
        public int RequestedGold { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public TradeOfferStatus Status { get; set; } = TradeOfferStatus.Pending;
    }

    /// <summary>Репозиторий торговых данных NPC (цены, множители).</summary>
    public interface ITradeRepository
    {
        Task<TradeItem?> GetItemInfoAsync(string itemId, CancellationToken cancellationToken = default);
        Task<float> GetBuyMultiplierAsync(Guid npcId, Guid characterId, CancellationToken cancellationToken = default);
        Task<float> GetSellMultiplierAsync(Guid npcId, Guid characterId, CancellationToken cancellationToken = default);
    }

    /// <summary>Репозиторий торговых предложений.</summary>
    public interface ITradeOfferRepository
    {
        Task AddAsync(TradeOffer offer, CancellationToken cancellationToken = default);
        Task<TradeOffer?> GetByIdAsync(Guid offerId, CancellationToken cancellationToken = default);
        Task UpdateAsync(TradeOffer offer, CancellationToken cancellationToken = default);
        Task RemoveAsync(Guid offerId, CancellationToken cancellationToken = default);
        /// <summary>Возвращает все торговые предложения.</summary>
        Task<List<TradeOffer>> GetAllAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Сервис торговли: покупка/продажа у NPC, обмен между игроками.
    /// Включает проверки прав, лимиты на количество предметов и публикацию событий.
    /// </summary>
    public class TradeService
    {
        private readonly ICommandBus _commandBus;
        private readonly CharacterProjection _characterProjection;
        private readonly PermissionChecker _permissionChecker;
        private readonly ITradeRepository _tradeRepo;
        private readonly ITradeOfferRepository _offerRepo;
        private readonly IEventBus _eventBus;
        private readonly ILogger<TradeService> _logger;
        private readonly TechnicalLimits _limits;
        private readonly ICurrentUserService _currentUserService;
        private readonly ICharacterOwnershipRepository _ownershipRepository;

        public TradeService(
            ICommandBus commandBus,
            CharacterProjection characterProjection,
            PermissionChecker permissionChecker,
            ITradeRepository tradeRepo,
            ITradeOfferRepository offerRepo,
            IEventBus eventBus,
            IOptions<Settings> settings,
            ICurrentUserService currentUserService,
            ICharacterOwnershipRepository ownershipRepository,
            ILogger<TradeService>? logger = null)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
            _tradeRepo = tradeRepo ?? throw new ArgumentNullException(nameof(tradeRepo));
            _offerRepo = offerRepo ?? throw new ArgumentNullException(nameof(offerRepo));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? NullLogger<TradeService>.Instance;
            _limits = settings?.Value.Limits ?? throw new ArgumentNullException(nameof(settings));
            _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
            _ownershipRepository = ownershipRepository ?? throw new ArgumentNullException(nameof(ownershipRepository));
        }

        // ==================== Покупка и продажа у NPC ====================

        /// <summary>Покупает предмет у NPC-торговца.</summary>
        public async Task BuyItemFromNpcAsync(
            Guid characterId,
            Guid npcId,
            string itemId,
            int quantity = 1,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateGuid(npcId, nameof(npcId));
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Количество должно быть положительным.");
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureCanControlCharacterAsync(characterId, cancellationToken);
            var character = await GetCharacterOrThrowAsync(characterId, cancellationToken);

            var itemInfo = await _tradeRepo.GetItemInfoAsync(itemId, cancellationToken)
                           ?? throw new InvalidOperationException("Предмет не найден в торговом репозитории.");

            float buyMultiplier = await _tradeRepo.GetBuyMultiplierAsync(npcId, characterId, cancellationToken);
            int totalCostGold = (int)(itemInfo.BasePriceGold * quantity * buyMultiplier);

            if (character.Gold < totalCostGold)
                throw new InvalidOperationException($"Недостаточно золота. Требуется: {totalCostGold}, доступно: {character.Gold}.");

            if (totalCostGold > 0)
                await _commandBus.SendAsync(new SpendGold(characterId, totalCostGold), cancellationToken);
            await _commandBus.SendAsync(new AddInventoryItem(characterId, itemId, itemInfo.ItemName, quantity), cancellationToken);

            _logger.LogInformation("Персонаж {CharacterId} купил {Quantity} x {ItemId} у NPC {NpcId} за {Cost} золота",
                characterId, quantity, itemId, npcId, totalCostGold);
        }

        /// <summary>Продаёт предмет NPC-торговцу.</summary>
        public async Task SellItemToNpcAsync(
            Guid characterId,
            Guid npcId,
            string itemId,
            int quantity = 1,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateGuid(npcId, nameof(npcId));
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Количество должно быть положительным.");
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureCanControlCharacterAsync(characterId, cancellationToken);
            var character = await GetCharacterOrThrowAsync(characterId, cancellationToken);

            var itemInfo = await _tradeRepo.GetItemInfoAsync(itemId, cancellationToken)
                           ?? throw new InvalidOperationException("Предмет не найден в торговом репозитории.");

            var inventoryItem = character.Inventory.FirstOrDefault(i => i.ItemId == itemId);
            if (inventoryItem == null || inventoryItem.Quantity < quantity)
                throw new InvalidOperationException("У персонажа недостаточно этого предмета для продажи.");

            float sellMultiplier = await _tradeRepo.GetSellMultiplierAsync(npcId, characterId, cancellationToken);
            int totalGold = (int)(itemInfo.BasePriceGold * quantity * sellMultiplier);

            await _commandBus.SendAsync(new RemoveInventoryItem(characterId, itemId, quantity), cancellationToken);
            if (totalGold > 0)
                await _commandBus.SendAsync(new AddGold(characterId, totalGold), cancellationToken);

            _logger.LogInformation("Персонаж {CharacterId} продал {Quantity} x {ItemId} NPC {NpcId} за {Gold} золота",
                characterId, quantity, itemId, npcId, totalGold);
        }

        // ==================== Торговля между игроками ====================

        /// <summary>Создаёт предложение обмена между двумя персонажами.</summary>
        public async Task<TradeOffer> ProposeTradeAsync(
            Guid fromCharacterId,
            Guid toCharacterId,
            List<TradeItem> offeredItems,
            int offeredGold,
            List<TradeItem> requestedItems,
            int requestedGold,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(fromCharacterId, nameof(fromCharacterId));
            ValidateGuid(toCharacterId, nameof(toCharacterId));
            if (fromCharacterId == toCharacterId)
                throw new InvalidOperationException("Нельзя торговать с самим собой.");
            if (offeredItems == null) throw new ArgumentNullException(nameof(offeredItems));
            if (requestedItems == null) throw new ArgumentNullException(nameof(requestedItems));
            if (offeredGold < 0) throw new ArgumentOutOfRangeException(nameof(offeredGold), "Золото не может быть отрицательным.");
            if (requestedGold < 0) throw new ArgumentOutOfRangeException(nameof(requestedGold), "Золото не может быть отрицательным.");
            cancellationToken.ThrowIfCancellationRequested();

            // Проверка лимита количества предметов
            int totalItems = offeredItems.Sum(i => i.Quantity) + requestedItems.Sum(i => i.Quantity);
            if (totalItems > _limits.MaxTradeItemsPerOffer)
                throw new InvalidOperationException(
                    $"Превышен лимит предметов в предложении (максимум {_limits.MaxTradeItemsPerOffer}).");

            await EnsureCanControlCharacterAsync(fromCharacterId, cancellationToken);
            var fromChar = await GetCharacterOrThrowAsync(fromCharacterId, cancellationToken);
            var toChar = await GetCharacterOrThrowAsync(toCharacterId, cancellationToken);

            foreach (var item in offeredItems)
            {
                if (item == null) continue;
                var invItem = fromChar.Inventory.FirstOrDefault(i => i.ItemId == item.ItemId);
                if (invItem == null || invItem.Quantity < item.Quantity)
                    throw new InvalidOperationException($"У вас недостаточно предмета «{item.ItemName}» для предложения.");
            }
            if (fromChar.Gold < offeredGold)
                throw new InvalidOperationException("Недостаточно золота для предложения.");

            var offer = new TradeOffer
            {
                OfferId = Guid.NewGuid(),
                FromCharacterId = fromCharacterId,
                ToCharacterId = toCharacterId,
                OfferedItems = offeredItems,
                OfferedGold = offeredGold,
                RequestedItems = requestedItems,
                RequestedGold = requestedGold,
                Status = TradeOfferStatus.Pending
            };

            await _offerRepo.AddAsync(offer, cancellationToken);

            await _eventBus.PublishAsync(new TradeOfferCreated(
                offer.OfferId,
                fromCharacterId,
                toCharacterId,
                offeredItems,
                offeredGold,
                requestedItems,
                requestedGold,
                DateTime.UtcNow), cancellationToken);

            _logger.LogInformation("Создано торговое предложение {OfferId} от {From} к {To}",
                offer.OfferId, fromCharacterId, toCharacterId);

            return offer;
        }

        public async Task<List<TradeOffer>> GetOffersAsync(CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.GetCurrentUserId();
            var ownedCharacterIds = await _ownershipRepository.GetOwnedCharacterIdsAsync(userId, cancellationToken);

            var allOffers = await _offerRepo.GetAllAsync(cancellationToken);
            return allOffers
                .Where(o => ownedCharacterIds.Contains(o.FromCharacterId) || ownedCharacterIds.Contains(o.ToCharacterId))
                .ToList();
        }

        /// <summary>Принимает торговое предложение, выполняя обмен.</summary>
        public async Task AcceptTradeAsync(Guid offerId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(offerId, nameof(offerId));
            cancellationToken.ThrowIfCancellationRequested();

            var offer = await _offerRepo.GetByIdAsync(offerId, cancellationToken)
                        ?? throw new InvalidOperationException("Торговое предложение не найдено.");
            if (offer.Status != TradeOfferStatus.Pending)
                throw new InvalidOperationException("Торговое предложение не активно.");

            await EnsureCanControlCharacterAsync(offer.ToCharacterId, cancellationToken);
            var toChar = await GetCharacterOrThrowAsync(offer.ToCharacterId, cancellationToken);

            foreach (var item in offer.RequestedItems)
            {
                if (item == null) continue;
                var invItem = toChar.Inventory.FirstOrDefault(i => i.ItemId == item.ItemId);
                if (invItem == null || invItem.Quantity < item.Quantity)
                    throw new InvalidOperationException($"У вас недостаточно предмета «{item.ItemName}» для завершения обмена.");
            }
            if (toChar.Gold < offer.RequestedGold)
                throw new InvalidOperationException("Недостаточно золота для завершения обмена.");

            // Снимаем у обеих сторон
            foreach (var item in offer.OfferedItems)
                await _commandBus.SendAsync(new RemoveInventoryItem(offer.FromCharacterId, item.ItemId, item.Quantity), cancellationToken);
            if (offer.OfferedGold > 0)
                await _commandBus.SendAsync(new SpendGold(offer.FromCharacterId, offer.OfferedGold), cancellationToken);

            foreach (var item in offer.RequestedItems)
                await _commandBus.SendAsync(new RemoveInventoryItem(offer.ToCharacterId, item.ItemId, item.Quantity), cancellationToken);
            if (offer.RequestedGold > 0)
                await _commandBus.SendAsync(new SpendGold(offer.ToCharacterId, offer.RequestedGold), cancellationToken);

            // Начисляем встречно
            foreach (var item in offer.OfferedItems)
                await _commandBus.SendAsync(new AddInventoryItem(offer.ToCharacterId, item.ItemId, item.ItemName, item.Quantity), cancellationToken);
            if (offer.OfferedGold > 0)
                await _commandBus.SendAsync(new AddGold(offer.ToCharacterId, offer.OfferedGold), cancellationToken);

            foreach (var item in offer.RequestedItems)
                await _commandBus.SendAsync(new AddInventoryItem(offer.FromCharacterId, item.ItemId, item.ItemName, item.Quantity), cancellationToken);
            if (offer.RequestedGold > 0)
                await _commandBus.SendAsync(new AddGold(offer.FromCharacterId, offer.RequestedGold), cancellationToken);

            offer.Status = TradeOfferStatus.Accepted;
            await _offerRepo.UpdateAsync(offer, cancellationToken);

            await _eventBus.PublishAsync(new TradeOfferAccepted(offer.OfferId, DateTime.UtcNow), cancellationToken);
            _logger.LogInformation("Торговое предложение {OfferId} принято и выполнено", offer.OfferId);
        }

        /// <summary>Отклоняет торговое предложение.</summary>
        public async Task DeclineTradeAsync(Guid offerId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(offerId, nameof(offerId));
            cancellationToken.ThrowIfCancellationRequested();

            var offer = await _offerRepo.GetByIdAsync(offerId, cancellationToken)
                        ?? throw new InvalidOperationException("Торговое предложение не найдено.");
            if (offer.Status != TradeOfferStatus.Pending)
                throw new InvalidOperationException("Торговое предложение не активно.");

            await EnsureCanControlCharacterAsync(offer.ToCharacterId, cancellationToken);

            offer.Status = TradeOfferStatus.Declined;
            await _offerRepo.UpdateAsync(offer, cancellationToken);

            await _eventBus.PublishAsync(new TradeOfferDeclined(offer.OfferId, DateTime.UtcNow), cancellationToken);
            _logger.LogInformation("Торговое предложение {OfferId} отклонено", offer.OfferId);
        }

        /// <summary>Отменяет исходящее торговое предложение.</summary>
        public async Task CancelTradeOfferAsync(Guid offerId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(offerId, nameof(offerId));
            cancellationToken.ThrowIfCancellationRequested();

            var offer = await _offerRepo.GetByIdAsync(offerId, cancellationToken)
                        ?? throw new InvalidOperationException("Торговое предложение не найдено.");
            if (offer.Status != TradeOfferStatus.Pending)
                throw new InvalidOperationException("Торговое предложение не активно.");

            await EnsureCanControlCharacterAsync(offer.FromCharacterId, cancellationToken);

            offer.Status = TradeOfferStatus.Cancelled;
            await _offerRepo.UpdateAsync(offer, cancellationToken);

            await _eventBus.PublishAsync(new TradeOfferCancelled(offer.OfferId, DateTime.UtcNow), cancellationToken);
            _logger.LogInformation("Торговое предложение {OfferId} отменено", offer.OfferId);
        }

        // ==================== Вспомогательные методы ====================

        private async Task EnsureCanControlCharacterAsync(Guid characterId, CancellationToken ct)
        {
            if (!await _permissionChecker.CanControlCharacterAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для управления этим персонажем.");
        }

        private async Task<CharacterDto> GetCharacterOrThrowAsync(Guid characterId, CancellationToken ct)
        {
            var character = await _characterProjection.GetById(characterId, ct);
            if (character == null)
                throw new InvalidOperationException("Персонаж не найден.");
            return character;
        }

        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не должен быть пустым: {paramName}", paramName);
        }
    }
}