#nullable enable
using System;
using System.Diagnostics;
using OpenTelemetry.Trace;
using Microsoft.Extensions.Logging;

namespace dnd_game.infrastructure.monitoring
{
    /// <summary>
    /// Интерфейс распределённой трассировки для приложения DnD.
    /// </summary>
    public interface ITracer
    {
        /// <summary>Начинает новый спан с указанным именем.</summary>
        IDisposable StartSpan(string name);

        /// <summary>Начинает дочерний спан с указанным родительским контекстом.</summary>
        IDisposable StartSpan(string name, SpanContext parentContext);

        /// <summary>Текущий активный спан (если есть).</summary>
        Activity? CurrentSpan { get; }

        /// <summary>Устанавливает тег на текущем спане.</summary>
        void SetTag(string key, string? value);

        /// <summary>Добавляет событие на текущий спан.</summary>
        void AddEvent(string eventName);

        /// <summary>Записывает исключение в текущий спан.</summary>
        void RecordException(Exception ex);
    }

    /// <summary>
    /// Реализация трейсера на основе OpenTelemetry.
    /// Интегрируется с ActivitySource и позволяет экспортировать трассы в Jaeger, Zipkin и т.д.
    /// </summary>
    public class OpenTelemetryTracer(ActivitySource activitySource, ILogger<OpenTelemetryTracer> logger) : ITracer
    {
        private readonly ActivitySource _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
        private readonly ILogger<OpenTelemetryTracer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public Activity? CurrentSpan => Activity.Current;

        public IDisposable StartSpan(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя спана не может быть пустым.", nameof(name));

            var activity = _activitySource.StartActivity(name, ActivityKind.Internal);
            if (activity == null)
            {
                _logger.LogTrace("Спан '{SpanName}' не создан (нет слушателей).", name);
                return NoopSpan.Instance;
            }
            return new OpenTelemetrySpan(activity);
        }

        public IDisposable StartSpan(string name, SpanContext parentContext)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя спана не может быть пустым.", nameof(name));

            var activity = _activitySource.StartActivity(name, ActivityKind.Internal, parentContext);
            if (activity == null)
                return NoopSpan.Instance;
            return new OpenTelemetrySpan(activity);
        }

        public void SetTag(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Ключ тега не может быть пустым.", nameof(key));

            Activity.Current?.SetTag(key, value);
        }

        public void AddEvent(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                throw new ArgumentException("Имя события не может быть пустым.", nameof(eventName));

            Activity.Current?.AddEvent(new ActivityEvent(eventName));
        }

        public void RecordException(Exception ex)
        {
            ArgumentNullException.ThrowIfNull(ex);
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // Добавляем информацию об исключении через теги и событие для совместимости
            Activity.Current?.AddTag("exception.type", ex.GetType().FullName);
            Activity.Current?.AddTag("exception.message", ex.Message);
            Activity.Current?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message }
            }));
        }

        private sealed class OpenTelemetrySpan(Activity activity) : IDisposable
        {
            private readonly Activity _activity = activity;

            public void Dispose() => _activity.Dispose();
        }

        private sealed class NoopSpan : IDisposable
        {
            public static readonly NoopSpan Instance = new();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Упрощённый трейсер без внешней зависимости (использует System.Diagnostics.Activity).
    /// </summary>
    public class SimpleTracer(ILogger<SimpleTracer> logger) : ITracer
    {
        private readonly ActivitySource _activitySource = new("DnD.Game");
        private readonly ILogger<SimpleTracer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public Activity? CurrentSpan => Activity.Current;

        public IDisposable StartSpan(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя спана не может быть пустым.", nameof(name));

            var activity = _activitySource.StartActivity(name, ActivityKind.Internal);
            if (activity == null) return NoopDisposable.Instance;
            _logger.LogTrace("Трассировочный спан запущен: {SpanName} ({SpanId})",
                activity.OperationName, activity.SpanId);
            return activity;
        }

        public IDisposable StartSpan(string name, SpanContext parentContext)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя спана не может быть пустым.", nameof(name));

            var activity = _activitySource.StartActivity(name, ActivityKind.Internal, parentContext);
            if (activity == null) return NoopDisposable.Instance;
            _logger.LogTrace("Дочерний трассировочный спан запущен: {SpanName} ({SpanId})",
                activity.OperationName, activity.SpanId);
            return activity;
        }

        public void SetTag(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Ключ тега не может быть пустым.", nameof(key));
            Activity.Current?.SetTag(key, value);
        }

        public void AddEvent(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                throw new ArgumentException("Имя события не может быть пустым.", nameof(eventName));
            Activity.Current?.AddEvent(new ActivityEvent(eventName));
        }

        public void RecordException(Exception ex)
        {
            ArgumentNullException.ThrowIfNull(ex);
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Activity.Current?.AddTag("exception.type", ex.GetType().FullName);
            Activity.Current?.AddTag("exception.message", ex.Message);
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Расширения для удобного создания трассировочных спанов с атрибутами DnD.
    /// </summary>
    public static class TracerExtensions
    {
        public static IDisposable StartCommandSpan(this ITracer tracer, string commandType, Guid userId, Guid sessionId)
        {
            var span = tracer.StartSpan($"Command.{commandType}");
            tracer.SetTag("command.type", commandType);
            tracer.SetTag("user.id", userId.ToString());
            tracer.SetTag("session.id", sessionId.ToString());
            return span;
        }

        public static IDisposable StartQuerySpan(this ITracer tracer, string queryType, Guid userId, Guid sessionId)
        {
            var span = tracer.StartSpan($"Query.{queryType}");
            tracer.SetTag("query.type", queryType);
            tracer.SetTag("user.id", userId.ToString());
            tracer.SetTag("session.id", sessionId.ToString());
            return span;
        }

        public static IDisposable StartEventSpan(this ITracer tracer, string eventType)
        {
            var span = tracer.StartSpan($"Event.{eventType}");
            tracer.SetTag("event.type", eventType);
            return span;
        }

        public static IDisposable StartCombatSpan(this ITracer tracer, Guid combatId, int round)
        {
            var span = tracer.StartSpan($"Combat.Round{round}");
            tracer.SetTag("combat.id", combatId.ToString());
            tracer.SetTag("combat.round", round.ToString());
            return span;
        }

        public static IDisposable StartCharacterActionSpan(this ITracer tracer, Guid characterId, string action)
        {
            var span = tracer.StartSpan($"Character.{action}");
            tracer.SetTag("character.id", characterId.ToString());
            tracer.SetTag("action", action);
            return span;
        }
    }
}