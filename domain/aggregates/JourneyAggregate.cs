#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.domain.events;

namespace dnd_game.domain.aggregates
{
    /// <summary>
    /// Агрегат путешествия группы. Инкапсулирует состояние путешествия,
    /// правила передвижения, навигации, потребления ресурсов и истощения.
    /// Идентификатор агрегата совпадает с идентификатором партии (PartyId),
    /// так как у группы может быть только одно активное путешествие.
    /// </summary>
    public class JourneyAggregate : AggregateRoot
    {
        public Guid PartyId => Id; // JourneyId = PartyId
        public Guid RouteId { get; private set; }
        public string Pace { get; private set; } = "Normal";
        public bool IsActive { get; private set; }
        public int CurrentDay { get; private set; } = 1;
        public int CurrentHour { get; private set; }
        public string CurrentTerrain { get; private set; } = string.Empty;
        public int NavigationSuccesses { get; private set; }
        public int NavigationFailures { get; private set; }
        public bool IsLost { get; private set; }
        public int ExhaustionLevel { get; private set; }
        public Dictionary<string, int> Resources { get; private set; } = new()
        {
            { "Food", 10 },
            { "Water", 10 }
        };

        // ---------- Конструкторы ----------
        public JourneyAggregate(Guid partyId, Guid routeId, string pace)
        {
            if (partyId == Guid.Empty) throw new ArgumentException("Идентификатор партии не может быть пустым.", nameof(partyId));
            if (routeId == Guid.Empty) throw new ArgumentException("Идентификатор маршрута не может быть пустым.", nameof(routeId));
            if (string.IsNullOrWhiteSpace(pace)) throw new ArgumentException("Темп путешествия не может быть пустым.", nameof(pace));

            ApplyChange(new JourneyStarted(partyId, partyId, routeId, pace, DateTime.UtcNow));
        }

        // Для event sourcing
        public JourneyAggregate() { }

        // ---------- Применение событий ----------
        protected override void ApplyEvent(IDomainEvent @event)
        {
            switch (@event)
            {
                case JourneyStarted e:
                    Id = e.JourneyId;
                    RouteId = e.RouteId;
                    Pace = e.Pace;
                    IsActive = true;
                    CurrentDay = 1;
                    CurrentHour = 0;
                    CurrentTerrain = string.Empty;
                    IsLost = false;
                    ExhaustionLevel = 0;
                    Resources = new Dictionary<string, int> { { "Food", 10 }, { "Water", 10 } };
                    break;

                case JourneyEnded:
                    IsActive = false;
                    break;

                case JourneyDayAdvanced e:
                    CurrentTerrain = e.Terrain;
                    CurrentHour += e.HoursTraveled;
                    // Каждые 24 часа увеличивают день
                    while (CurrentHour >= 24)
                    {
                        CurrentHour -= 24;
                        CurrentDay++;
                    }
                    break;

                case JourneyPaceChanged e:
                    Pace = e.NewPace;
                    break;

                case ForcedMarchPerformed e:
                    CurrentHour += e.AdditionalHours;
                    while (CurrentHour >= 24)
                    {
                        CurrentHour -= 24;
                        CurrentDay++;
                    }
                    // Если марш длился больше 8 часов, персонажи получают истощение
                    if (e.AdditionalHours > 8)
                        ExhaustionLevel = Math.Min(ExhaustionLevel + 1, 5);
                    break;

                case NavigationCheckPerformed e:
                    if (e.Success) NavigationSuccesses++;
                    else NavigationFailures++;
                    break;

                case PartyLost:
                    IsLost = true;
                    break;

                case ResourcesConsumed e:
                    Resources["Food"] = Math.Max(0, Resources["Food"] - e.Days);
                    Resources["Water"] = Math.Max(0, Resources["Water"] - e.Days);
                    break;

                case RandomEncounterChecked:
                    // Факт проверки может храниться, но не обязателен для состояния
                    break;

                case ExhaustionApplied e:
                    ExhaustionLevel = Math.Clamp(e.ExhaustionLevel, 0, 5);
                    break;
            }
        }

        // ---------- Команды (методы) ----------

        public void EndJourney()
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие уже завершено.");
            ApplyChange(new JourneyEnded(Id, DateTime.UtcNow));
        }

        public void AdvanceDay(string terrain, int hoursTraveled, int navigationCheckResult)
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие не активно.");
            if (string.IsNullOrWhiteSpace(terrain)) throw new ArgumentException("Тип местности не может быть пустым.", nameof(terrain));
            if (hoursTraveled < 0) throw new ArgumentOutOfRangeException(nameof(hoursTraveled), "Количество часов не может быть отрицательным.");

            // navigationCheckResult — это результат броска навигации (не используется напрямую,
            // но может влиять на вероятность потеряться; для простоты игнорируем)
            ApplyChange(new JourneyDayAdvanced(Id, terrain, hoursTraveled, navigationCheckResult, DateTime.UtcNow));
        }

        public void ChangePace(string newPace)
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие не активно.");
            if (string.IsNullOrWhiteSpace(newPace)) throw new ArgumentException("Темп не может быть пустым.", nameof(newPace));
            ApplyChange(new JourneyPaceChanged(Id, newPace, DateTime.UtcNow));
        }

        public void ForcedMarch(int additionalHours)
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие не активно.");
            if (additionalHours <= 0) throw new ArgumentOutOfRangeException(nameof(additionalHours), "Дополнительные часы должны быть положительными.");
            ApplyChange(new ForcedMarchPerformed(Id, additionalHours, DateTime.UtcNow));
        }

        public void PerformNavigationCheck(int roll, int wisdomModifier, bool isProficient)
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие не активно.");
            if (roll < 1 || roll > 20) throw new ArgumentOutOfRangeException(nameof(roll), "Бросок d20 должен быть от 1 до 20.");

            int dc = 10; // базовая сложность навигации
            bool success = roll + wisdomModifier + (isProficient ? 2 : 0) >= dc;
            ApplyChange(new NavigationCheckPerformed(Id, roll, wisdomModifier, isProficient, success, DateTime.UtcNow));
            if (!success)
                ApplyChange(new PartyLost(Id, DateTime.UtcNow));
        }

        public void MarkAsLost()
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие не активно.");
            ApplyChange(new PartyLost(Id, DateTime.UtcNow));
        }

        public void ConsumeResources(int days)
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие не активно.");
            if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days), "Количество дней должно быть положительным.");
            ApplyChange(new ResourcesConsumed(Id, days, DateTime.UtcNow));
        }

        public void CheckRandomEncounter(string terrain)
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие не активно.");
            if (string.IsNullOrWhiteSpace(terrain)) throw new ArgumentException("Тип местности не может быть пустым.", nameof(terrain));

            // Простая логика: случайная встреча происходит с вероятностью 15%
            bool encounter = Random.Shared.NextDouble() < 0.15;
            ApplyChange(new RandomEncounterChecked(Id, terrain, encounter, DateTime.UtcNow));
        }

        public void ApplyExhaustion(int level)
        {
            if (!IsActive) throw new InvalidOperationException("Путешествие не активно.");
            if (level < 0 || level > 5) throw new ArgumentOutOfRangeException(nameof(level), "Уровень истощения должен быть от 0 до 5.");
            ApplyChange(new ExhaustionApplied(Id, level, DateTime.UtcNow));
        }
    }
}