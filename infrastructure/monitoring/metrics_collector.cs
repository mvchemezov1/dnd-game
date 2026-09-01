#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace dnd_game.infrastructure.monitoring
{
    /// <summary>
    /// Интерфейс сбора метрик, используемый всеми компонентами игры.
    /// </summary>
    public interface IMetricsCollector
    {
        /// <summary>Увеличивает счётчик на указанное значение.</summary>
        void IncrementCounter(string metricName, int value = 1);

        /// <summary>Устанавливает значение датчика (gauge).</summary>
        void SetGauge(string metricName, double value);

        /// <summary>Записывает значение в гистограмму.</summary>
        void RecordHistogram(string metricName, double value);
    }

    /// <summary>
    /// Реализация сборщика метрик на основе System.Diagnostics.Metrics.
    /// Метрики доступны для экспорта в Prometheus, Application Insights и другие системы.
    /// </summary>
    public class MetricsCollector : IMetricsCollector, IDisposable
    {
        private readonly Meter _meter;
        private readonly ILogger<MetricsCollector> _logger;
        private readonly ConcurrentDictionary<string, Counter<int>> _counters = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ObservableGauge<double>> _gauges = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, double> _gaugeValues = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Создаёт сборщик метрик с предопределёнными метриками для DnD.
        /// </summary>
        /// <param name="logger">Логгер для записи предупреждений.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="logger"/> равен null.</exception>
        public MetricsCollector(ILogger<MetricsCollector> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _meter = new Meter("DnD.Game", "1.0.0");
            InitializeDefaultMetrics();
        }

        /// <summary>
        /// Регистрирует стандартные метрики, используемые в игре.
        /// </summary>
        private void InitializeDefaultMetrics()
        {
            // Счётчики событий
            CreateCounter("dnd.events.total", "Всего доменных событий");
            CreateCounter("dnd.commands.total", "Всего выполненных команд");
            CreateCounter("dnd.queries.total", "Всего выполненных запросов");

            // Счётчики персонажей
            CreateCounter("dnd.characters.created", "Создано персонажей");
            CreateCounter("dnd.deaths.total", "Смерти персонажей");
            CreateCounter("dnd.levels.total", "Всего получено уровней");

            // Боевые метрики
            CreateCounter("dnd.combat.started", "Начато боёв");
            CreateCounter("dnd.combat.ended", "Завершено боёв");
            CreateHistogram("dnd.combat.duration_seconds", "Длительность боя");
            CreateCounter("dnd.attacks.total", "Всего атак");
            CreateCounter("dnd.attacks.hit", "Успешных атак");
            CreateCounter("dnd.attacks.miss", "Промахов");
            CreateCounter("dnd.attacks.critical_hit", "Критических попаданий");
            CreateCounter("dnd.damage.total", "Всего нанесено урона");
            CreateHistogram("dnd.damage.amount", "Распределение урона");

            // Урон по типам
            foreach (var dmgType in new[] { "fire", "cold", "lightning", "acid", "poison", "radiant", "necrotic", "psychic", "force", "bludgeoning", "piercing", "slashing" })
                CreateCounter($"dnd.damage.by_type.{dmgType}", $"Урон ({dmgType})");

            // Лечение
            CreateCounter("dnd.healing.total", "Всего исцелено");
            CreateHistogram("dnd.healing.amount", "Величина исцеления");

            // Заклинания
            CreateCounter("dnd.spells.cast", "Произнесено заклинаний");
            CreateHistogram("dnd.spell.level", "Распределение уровня заклинаний");
            CreateCounter("dnd.spell_slots.used", "Использовано ячеек заклинаний");

            // Навыки
            CreateCounter("dnd.skill_checks.total", "Всего проверок навыков");
            CreateHistogram("dnd.skill_checks.roll", "Результаты бросков навыков");

            // Отдых
            CreateCounter("dnd.rest.started", "Начато отдыхов");
            CreateCounter("dnd.rest.ended", "Завершено отдыхов");

            // Социальные взаимодействия
            CreateCounter("dnd.social.interactions", "Начато социальных взаимодействий");

            // Ловушки
            CreateCounter("dnd.traps.triggered", "Активировано ловушек");

            // Gauge – количество активных боёв
            CreateGauge("dnd.combat.active", "Количество активных боёв");
        }

        /// <inheritdoc />
        public void IncrementCounter(string metricName, int value = 1)
        {
            ValidateMetricName(metricName);
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Значение не может быть отрицательным.");

            try
            {
                if (_counters.TryGetValue(metricName, out var counter))
                {
                    counter.Add(value);
                }
                else
                {
                    // Динамически создаём счётчик
                    var newCounter = _meter.CreateCounter<int>(metricName);
                    _counters[metricName] = newCounter;
                    newCounter.Add(value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось увеличить счётчик {MetricName}", metricName);
            }
        }

        /// <inheritdoc />
        public void SetGauge(string metricName, double value)
        {
            ValidateMetricName(metricName);
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Значение должно быть конечным числом.");

            _gaugeValues[metricName] = value;

            // Если gauge ещё не создан (например, динамически), создаём его
            if (!_gauges.ContainsKey(metricName))
            {
                CreateGauge(metricName, $"Датчик: {metricName}");
            }
        }

        /// <inheritdoc />
        public void RecordHistogram(string metricName, double value)
        {
            ValidateMetricName(metricName);
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Значение не может быть отрицательным.");

            if (_histograms.TryGetValue(metricName, out var histogram))
            {
                histogram.Record(value);
            }
            else
            {
                var newHistogram = _meter.CreateHistogram<double>(metricName);
                _histograms[metricName] = newHistogram;
                newHistogram.Record(value);
            }
        }

        // ---------- Специфичные методы D&D ----------

        /// <summary>Увеличивает счётчик событий определённого типа.</summary>
        public void IncrementEvent(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Тип события не может быть пустым.", nameof(eventType));
            IncrementCounter($"dnd.events.{eventType}");
        }

        /// <summary>Записывает длительность выполнения команды.</summary>
        public void RecordCommandDuration(string commandType, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(commandType))
                throw new ArgumentException("Тип команды не может быть пустым.", nameof(commandType));
            RecordHistogram($"dnd.commands.duration.{commandType}", duration.TotalMilliseconds);
        }

        /// <summary>Увеличивает счётчик урона по типу и записывает величину урона в гистограмму.</summary>
        public void IncrementDamageByType(string damageType, int amount)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                throw new ArgumentException("Тип урона не может быть пустым.", nameof(damageType));
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Величина урона не может быть отрицательной.");

            IncrementCounter($"dnd.damage.by_type.{damageType.ToLowerInvariant()}");
            RecordHistogram("dnd.damage.amount", amount);
        }

        /// <summary>Записывает информацию о произнесённом заклинании.</summary>
        public void RecordSpellCast(string spellName, int spellLevel)
        {
            if (string.IsNullOrWhiteSpace(spellName))
                throw new ArgumentException("Название заклинания не может быть пустым.", nameof(spellName));
            if (spellLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(spellLevel), "Уровень заклинания не может быть отрицательным.");

            IncrementCounter("dnd.spells.cast");
            IncrementCounter($"dnd.spells.cast.{spellName}");
            RecordHistogram("dnd.spell.level", spellLevel);
        }

        /// <summary>Устанавливает количество активных боёв.</summary>
        public void SetActiveCombatCount(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Количество боёв не может быть отрицательным.");
            SetGauge("dnd.combat.active", count);
        }

        // ---------- Внутренние методы создания метрик ----------

        private void CreateCounter(string name, string description)
        {
            if (!_counters.ContainsKey(name))
            {
                var counter = _meter.CreateCounter<int>(name, description: description);
                _counters[name] = counter;
            }
        }

        private void CreateGauge(string name, string description)
        {
            if (!_gauges.ContainsKey(name))
            {
                var gauge = _meter.CreateObservableGauge(name,
                    () => new Measurement<double>(_gaugeValues.GetValueOrDefault(name, 0)),
                    description: description);
                _gauges[name] = gauge;
            }
        }

        private void CreateHistogram(string name, string description)
        {
            if (!_histograms.ContainsKey(name))
            {
                var histogram = _meter.CreateHistogram<double>(name, description: description);
                _histograms[name] = histogram;
            }
        }

        private static void ValidateMetricName(string metricName)
        {
            if (string.IsNullOrWhiteSpace(metricName))
                throw new ArgumentException("Имя метрики не может быть пустым.", nameof(metricName));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _meter.Dispose();
            _counters.Clear();
            _gauges.Clear();
            _histograms.Clear();
            _gaugeValues.Clear();
            GC.SuppressFinalize(this);
        }
    }
}