#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.security;
using dnd_game.domain.events;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace dnd_game.presentation.api
{
    public class WebSocketEventDispatcher
    {
        private readonly IEventBus _eventBus;
        private readonly INetworkProtocol _protocol;
        private readonly ICharacterOwnershipRepository _ownershipRepository;
        private readonly PermissionChecker _permissionChecker;
        private readonly ILogger<WebSocketEventDispatcher> _logger;
        private readonly ConcurrentDictionary<Guid, WebSocketConnectionState> _connections = new();

        public WebSocketEventDispatcher(
            IEventBus eventBus,
            INetworkProtocol protocol,
            ICharacterOwnershipRepository ownershipRepository,
            PermissionChecker permissionChecker,
            ILogger<WebSocketEventDispatcher>? logger = null)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _ownershipRepository = ownershipRepository ?? throw new ArgumentNullException(nameof(ownershipRepository));
            _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
            _logger = logger ?? NullLogger<WebSocketEventDispatcher>.Instance;

            // Единственная подписка на все события
            _eventBus.Subscribe<IDomainEvent>(OnDomainEvent);
        }

        public void AddConnection(WebSocketConnectionState state)
        {
            _connections[state.ConnectionId] = state;
        }

        public void RemoveConnection(Guid connectionId)
        {
            _connections.TryRemove(connectionId, out _);
        }

        private async Task OnDomainEvent(IDomainEvent @event, CancellationToken ct)
        {
            foreach (var state in _connections.Values)
            {
                if (state.Socket.State != WebSocketState.Open)
                    continue;

                if (!await ShouldSendEventToSessionAsync(@event, state.SessionId, state.UserId, ct))
                    continue;

                var eventMsg = NetworkMessageFactory.FromEvent(@event);
                var bytes = _protocol.Encode(eventMsg);
                try
                {
                    await state.Socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Binary,
                        true,
                        ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось отправить событие подключению {ConnectionId}", state.ConnectionId);
                }
            }
        }

        private async Task<bool> ShouldSendEventToSessionAsync(
            IDomainEvent @event,
            Guid? sessionId,
            Guid? userId,
            CancellationToken ct)
        {
            if (!sessionId.HasValue)
                return false;

            if (@event is ISessionBoundEvent sessionEvent)
                return sessionEvent.GameSessionId == sessionId.Value;

            if (@event is ICampaignEvent campaignEvent)
                return campaignEvent.CampaignId == sessionId.Value;

            if (@event is ICharacterEvent characterEvent)
            {
                if (!userId.HasValue)
                    return false;

                var ownerId = await _ownershipRepository.GetOwnerIdAsync(characterEvent.CharacterId, ct);
                if (ownerId == userId.Value)
                    return true;

                if (await _ownershipRepository.IsNonPlayerCharacterAsync(characterEvent.CharacterId, ct))
                {
                    var npcCampaignId = await _ownershipRepository.GetCampaignIdAsync(characterEvent.CharacterId, ct);
                    return npcCampaignId == sessionId.Value;
                }

                return false;
            }

            return false;
        }
    }
}