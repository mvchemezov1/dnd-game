using dnd_game.domain.events;
using dnd_game.infrastructure.caching;
using dnd_game.infrastructure.event_store;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.projections
{
    /// <summary>
    /// Проекция боя: хранит текущее состояние всех боёв в памяти,
    /// обновляется событиями домена и предоставляет методы чтения с кэшированием.
    /// </summary>
    public class CombatProjection
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<Guid, CombatStatusDto> _state = [];
        private readonly Dictionary<Guid, int> _participantSpeed = [];
        private readonly ICacheProvider _cache;
        private readonly TimeSpan _cacheTtl;
        private readonly CharacterProjection? _characterProjection;

        public CombatProjection(
            ICacheProvider cache,
            CharacterProjection? characterProjection = null,
            TimeSpan? cacheTtl = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _characterProjection = characterProjection;
            _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(1);
        }

        /// <summary>
        /// Удаляет записи кэша, связанные с указанным боем.
        /// </summary>
        private void InvalidateCache(Guid combatId)
        {
            // Синхронное удаление для гарантии согласованности.
            _cache.RemoveAsync($"combat:{combatId}").GetAwaiter().GetResult();
            _cache.RemoveAsync($"combat:participants:{combatId}").GetAwaiter().GetResult();
            _cache.RemoveAsync($"combat:current:{combatId}").GetAwaiter().GetResult();
        }

        /// <summary>
        /// Возвращает скорость персонажа из словаря скоростей (по умолчанию 30 футов).
        /// </summary>
        private int GetSpeed(Guid characterId)
        {
            return _participantSpeed.TryGetValue(characterId, out var speed) ? speed : 30;
        }

        /// <summary>
        /// Обновляет одного участника в списке участников боя, создавая новый список.
        /// </summary>
        private static List<CombatParticipantDto> UpdateParticipant(
            List<CombatParticipantDto> participants,
            Guid characterId,
            Func<CombatParticipantDto, CombatParticipantDto> update)
        {
            return [.. participants.Select(p => p.CharacterId == characterId ? update(p) : p)];
        }

        // ==================== Обработчики событий ====================

        public void Apply(CombatStarted e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                var participants = e.Participants.Select(id => new CombatParticipantDto
                {
                    CharacterId = id,
                    Initiative = 0,
                    MovementRemaining = e.ParticipantSpeeds.TryGetValue(id, out var speed) ? speed : 30
                }).ToList();

                _state[e.CombatId] = new CombatStatusDto
                {
                    CombatId = e.CombatId,
                    IsActive = true,
                    Round = 0,
                    CurrentTurnIndex = -1,
                    Participants = participants,
                    PlayerCharacterIds = e.PlayerCharacterIds ?? []
                };
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatEnded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    _state[e.CombatId] = dto with { IsActive = false };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(InitiativeRolled e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(
                        dto.Participants,
                        e.CharacterId,
                        p => p with { Initiative = e.Initiative });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatRoundStarted e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    // Сортировка участников по убыванию инициативы и сброс индекса хода
                    var sorted = dto.Participants.OrderByDescending(p => p.Initiative).ToList();
                    _state[e.CombatId] = dto with
                    {
                        Round = e.Round,
                        Participants = sorted,
                        CurrentTurnIndex = 0
                    };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatTurnStarted e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = dto.Participants.Select(p =>
                        p.CharacterId == e.CharacterId
                            ? p with
                            {
                                IsCurrentTurn = true,
                                HasAction = true,
                                HasBonusAction = true,
                                HasReaction = true,
                                HasMovement = true,
                                MovementRemaining = GetSpeed(p.CharacterId)
                            }
                            : p with { IsCurrentTurn = false }
                    ).ToList();
                    int turnIndex = participants.FindIndex(p => p.CharacterId == e.CharacterId);
                    _state[e.CombatId] = dto with { Participants = participants, CurrentTurnIndex = turnIndex };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatTurnEnded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(dto.Participants, e.CharacterId, p => p with { IsCurrentTurn = false });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatRoundEnded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            // Ничего не меняем, просто инвалидируем кэш на случай внешних изменений.
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatActionTaken e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(dto.Participants, e.CharacterId, p => p with { HasAction = false });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatBonusActionTaken e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(dto.Participants, e.CharacterId, p => p with { HasBonusAction = false });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatReactionUsed e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(dto.Participants, e.CharacterId, p => p with { HasReaction = false });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatMovementUsed e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(
                        dto.Participants,
                        e.CharacterId,
                        p => p with { MovementRemaining = Math.Max(0, p.MovementRemaining - e.Feet) });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(ConditionAppliedToCombatant e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(
                        dto.Participants,
                        e.CharacterId,
                        p =>
                        {
                            var conditions = new List<string>(p.Conditions);
                            if (!conditions.Contains(e.Condition))
                                conditions.Add(e.Condition);
                            return p with { Conditions = conditions };
                        });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public async Task RebuildAsync(IEventStore eventStore, CancellationToken cancellationToken)
        {
            var allEvents = await eventStore.GetAllEvents();
            foreach (var e in allEvents)
            {
                if (e is IDomainEvent domainEvent)
                {
                    Apply(domainEvent);
                }
            }
            await _cache.RemoveAsync("combat:all", cancellationToken);
        }

        public void Apply(ConditionRemovedFromCombatant e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(
                        dto.Participants,
                        e.CharacterId,
                        p => p with { Conditions = [.. p.Conditions.Where(c => c != e.Condition)] });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatConcentrationStarted e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(dto.Participants, e.CharacterId, p => p with { Concentrating = true });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatConcentrationEnded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(dto.Participants, e.CharacterId, p => p with { Concentrating = false });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(ParticipantAddedToCombat e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var newParticipant = new CombatParticipantDto
                    {
                        CharacterId = e.CharacterId,
                        Initiative = e.Initiative,
                        MovementRemaining = GetSpeed(e.CharacterId)
                    };
                    var newList = dto.Participants
                        .Append(newParticipant)
                        .OrderByDescending(p => p.Initiative)
                        .ToList();
                    _state[e.CombatId] = dto with { Participants = newList };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(ParticipantRemovedFromCombat e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var newList = dto.Participants.Where(p => p.CharacterId != e.CharacterId).ToList();
                    _state[e.CombatId] = dto with { Participants = newList };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatActionReadied e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(
                        dto.Participants,
                        e.CharacterId,
                        p => p with
                        {
                            ReadyActionType = e.ActionType,
                            ReadyTriggerCondition = e.TriggerCondition,
                            HasReadiedAction = true
                        });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        public void Apply(CombatReadiedActionTriggered e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CombatId, out var dto))
                {
                    var participants = UpdateParticipant(
                        dto.Participants,
                        e.CharacterId,
                        p => p with
                        {
                            HasReadiedAction = false,
                            ReadyActionType = null,
                            ReadyTriggerCondition = null
                        });
                    _state[e.CombatId] = dto with { Participants = participants };
                }
            }
            InvalidateCache(e.CombatId);
        }

        // Обновление словаря скоростей от событий персонажа (не связано с конкретным боем)
        public void Apply(CharacterCreated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                _participantSpeed[e.CharacterId] = 30;
            }
        }

        public void Apply(SpeedUpdated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                _participantSpeed[e.CharacterId] = e.NewSpeed;
            }
        }

        // ==================== Диспетчеризация общих событий ====================

        public void Apply(IDomainEvent e)
        {
            ArgumentNullException.ThrowIfNull(e);
            switch (e)
            {
                case CombatStarted ev: Apply(ev); break;
                case CombatEnded ev: Apply(ev); break;
                case InitiativeRolled ev: Apply(ev); break;
                case CombatRoundStarted ev: Apply(ev); break;
                case CombatRoundEnded ev: Apply(ev); break;
                case CombatTurnStarted ev: Apply(ev); break;
                case CombatTurnEnded ev: Apply(ev); break;
                case CombatActionTaken ev: Apply(ev); break;
                case CombatBonusActionTaken ev: Apply(ev); break;
                case CombatReactionUsed ev: Apply(ev); break;
                case CombatMovementUsed ev: Apply(ev); break;
                case ConditionAppliedToCombatant ev: Apply(ev); break;
                case ConditionRemovedFromCombatant ev: Apply(ev); break;
                case CombatConcentrationStarted ev: Apply(ev); break;
                case CombatConcentrationEnded ev: Apply(ev); break;
                case ParticipantAddedToCombat ev: Apply(ev); break;
                case ParticipantRemovedFromCombat ev: Apply(ev); break;
                case CombatActionReadied ev: Apply(ev); break;
                case CombatReadiedActionTriggered ev: Apply(ev); break;
                case CharacterCreated ev: Apply(ev); break;
                case SpeedUpdated ev: Apply(ev); break;
                default:
                    // Игнорируем неизвестные события
                    break;
            }
        }

        // ==================== Методы чтения с кэшем ====================

        private async Task<CombatParticipantDto> EnrichParticipantAsync(
            CombatParticipantDto participant,
            CancellationToken ct)
        {
            if (_characterProjection == null)
                return participant;

            var character = await _characterProjection.GetById(participant.CharacterId, ct);
            if (character == null)
                return participant;

            return participant with
            {
                Name = character.Name,
                CurrentHitPoints = character.HitPoints,
                MaxHitPoints = character.MaxHitPoints,
                TemporaryHitPoints = character.TemporaryHitPoints,
                ArmorClass = character.ArmorClass
            };
        }

        private async Task<List<CombatParticipantDto>> EnrichParticipantsAsync(
            List<CombatParticipantDto> participants,
            CancellationToken ct)
        {
            var enriched = new List<CombatParticipantDto>(participants.Count);
            foreach (var p in participants)
                enriched.Add(await EnrichParticipantAsync(p, ct));
            return enriched;
        }

        // Переопределяем методы чтения с обогащением
        public async Task<CombatStatusDto?> GetStatus(Guid combatId, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"combat:{combatId}";
            var cached = await _cache.GetAsync<CombatStatusDto>(cacheKey, cancellationToken);
            if (cached != null)
            {
                var enrichedParticipants = await EnrichParticipantsAsync(cached.Participants, cancellationToken);
                return cached with { Participants = enrichedParticipants };
            }

            lock (_syncRoot)
            {
                if (_state.TryGetValue(combatId, out var dto))
                {
                    _cache.SetAsync(cacheKey, dto, _cacheTtl, cancellationToken).GetAwaiter().GetResult();
                    // Обогащаем после извлечения из кэша? Лучше обогащать всегда.
                    // Для простоты возвращаем как есть, но можно обогатить и здесь.
                    // Мы не будем кэшировать обогащённые данные, а будем обогащать при каждом запросе.
                    return dto;
                }
            }
            return null;
        }

        public async Task<List<CombatParticipantDto>> GetParticipants(
            Guid combatId,
            CancellationToken cancellationToken = default)
        {
            var status = await GetStatus(combatId, cancellationToken);
            return status?.Participants ?? [];
        }

        public async Task<CombatParticipantDto?> GetCurrentParticipant(
            Guid combatId,
            CancellationToken cancellationToken = default)
        {
            var status = await GetStatus(combatId, cancellationToken);
            if (status == null || status.CurrentTurnIndex < 0 || status.CurrentTurnIndex >= status.Participants.Count)
                return null;
            return status.Participants[status.CurrentTurnIndex];
        }
    }
}