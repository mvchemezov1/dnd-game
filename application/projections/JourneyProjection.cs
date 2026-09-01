#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.domain.events;
using dnd_game.infrastructure.caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace dnd_game.application.projections
{
    /// <summary>DTO состояния путешествия.</summary>
    public record JourneyStateDto(
        Guid PartyId,
        bool IsActive,
        string Pace,
        int CurrentDay,
        int CurrentHour,
        string Terrain,
        bool IsLost,
        int ExhaustionLevel,
        Dictionary<string, int> Resources);

    /// <summary>
    /// Проекция путешествий. Хранит состояние активных путешествий и обновляется событиями.
    /// </summary>
    public class JourneyProjection
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<Guid, JourneyStateDto> _state = new();
        private readonly ICacheProvider _cache;
        private readonly TimeSpan _cacheTtl;
        private readonly ILogger<JourneyProjection> _logger;

        public JourneyProjection(ICacheProvider cache, TimeSpan? cacheTtl = null, ILogger<JourneyProjection>? logger = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(5);
            _logger = logger ?? NullLogger<JourneyProjection>.Instance;
        }

        private void InvalidateCache(Guid partyId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _cache.RemoveAsync($"journey:{partyId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка при инвалидации кэша путешествия {PartyId}", partyId);
                }
            });
        }

        public void Apply(JourneyStarted e)
        {
            lock (_syncRoot)
            {
                _state[e.PartyId] = new JourneyStateDto(
                    PartyId: e.PartyId,
                    IsActive: true,
                    Pace: e.Pace,
                    CurrentDay: 1,
                    CurrentHour: 0,
                    Terrain: string.Empty,
                    IsLost: false,
                    ExhaustionLevel: 0,
                    Resources: new Dictionary<string, int> { { "Food", 10 }, { "Water", 10 } });
            }
            InvalidateCache(e.PartyId);
        }

        public void Apply(JourneyEnded e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.JourneyId, out var dto))
                {
                    _state[e.JourneyId] = dto with { IsActive = false };
                }
            }
            InvalidateCache(e.JourneyId);
        }

        public void Apply(JourneyDayAdvanced e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.JourneyId, out var dto))
                {
                    int currentHour = dto.CurrentHour + e.HoursTraveled;
                    int currentDay = dto.CurrentDay;
                    while (currentHour >= 24)
                    {
                        currentHour -= 24;
                        currentDay++;
                    }
                    _state[e.JourneyId] = dto with
                    {
                        CurrentDay = currentDay,
                        CurrentHour = currentHour,
                        Terrain = e.Terrain
                    };
                }
            }
            InvalidateCache(e.JourneyId);
        }

        public void Apply(JourneyPaceChanged e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.JourneyId, out var dto))
                {
                    _state[e.JourneyId] = dto with { Pace = e.NewPace };
                }
            }
            InvalidateCache(e.JourneyId);
        }

        public void Apply(ForcedMarchPerformed e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.JourneyId, out var dto))
                {
                    int currentHour = dto.CurrentHour + e.AdditionalHours;
                    int currentDay = dto.CurrentDay;
                    while (currentHour >= 24)
                    {
                        currentHour -= 24;
                        currentDay++;
                    }
                    int exhaustion = dto.ExhaustionLevel;
                    if (e.AdditionalHours > 8)
                        exhaustion = Math.Min(exhaustion + 1, 5);

                    _state[e.JourneyId] = dto with
                    {
                        CurrentDay = currentDay,
                        CurrentHour = currentHour,
                        ExhaustionLevel = exhaustion
                    };
                }
            }
            InvalidateCache(e.JourneyId);
        }

        public void Apply(NavigationCheckPerformed e)
        {
            // В DTO нет счётчиков успехов/провалов, можно добавить при необходимости.
            // Пока ничего не делаем.
        }

        public void Apply(PartyLost e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.JourneyId, out var dto))
                {
                    _state[e.JourneyId] = dto with { IsLost = true };
                }
            }
            InvalidateCache(e.JourneyId);
        }

        public void Apply(ResourcesConsumed e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.JourneyId, out var dto))
                {
                    var resources = new Dictionary<string, int>(dto.Resources);
                    if (resources.ContainsKey("Food"))
                        resources["Food"] = Math.Max(0, resources["Food"] - e.Days);
                    if (resources.ContainsKey("Water"))
                        resources["Water"] = Math.Max(0, resources["Water"] - e.Days);
                    _state[e.JourneyId] = dto with { Resources = resources };
                }
            }
            InvalidateCache(e.JourneyId);
        }

        public void Apply(RandomEncounterChecked e)
        {
            // Можно сохранять информацию о последнем энкаунтере, но не обязательно.
        }

        public void Apply(ExhaustionApplied e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.JourneyId, out var dto))
                {
                    _state[e.JourneyId] = dto with { ExhaustionLevel = Math.Clamp(e.ExhaustionLevel, 0, 5) };
                }
            }
            InvalidateCache(e.JourneyId);
        }

        // Диспетчеризация
        public void Apply(IDomainEvent @event)
        {
            switch (@event)
            {
                case JourneyStarted ev: Apply(ev); break;
                case JourneyEnded ev: Apply(ev); break;
                case JourneyDayAdvanced ev: Apply(ev); break;
                case JourneyPaceChanged ev: Apply(ev); break;
                case ForcedMarchPerformed ev: Apply(ev); break;
                case NavigationCheckPerformed ev: Apply(ev); break;
                case PartyLost ev: Apply(ev); break;
                case ResourcesConsumed ev: Apply(ev); break;
                case RandomEncounterChecked ev: Apply(ev); break;
                case ExhaustionApplied ev: Apply(ev); break;
            }
        }

        public async Task<JourneyStateDto?> GetByPartyIdAsync(Guid partyId, CancellationToken ct = default)
        {
            var cacheKey = $"journey:{partyId}";
            var cached = await _cache.GetAsync<JourneyStateDto>(cacheKey, ct);
            if (cached != null) return cached;

            lock (_syncRoot)
            {
                if (_state.TryGetValue(partyId, out var dto))
                {
                    _cache.SetAsync(cacheKey, dto, _cacheTtl, ct).GetAwaiter().GetResult();
                    return dto;
                }
            }
            return null;
        }
    }
}