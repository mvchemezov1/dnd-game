#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.queries;

namespace dnd_game.infrastructure.message_bus
{
    /// <summary>
    /// Контекст выполнения запроса. Содержит информацию о пользователе и игровой сессии.
    /// </summary>
    public class QueryContext
    {
        /// <summary>Идентификатор пользователя, выполняющего запрос.</summary>
        public Guid UserId { get; set; }

        /// <summary>Идентификатор игровой сессии (кампании), в рамках которой выполняется запрос.</summary>
        public Guid GameSessionId { get; set; }
    }

    /// <summary>
    /// Универсальная шина запросов для игры DnD.
    /// Реализация: InMemoryBus (см. in_memory_bus.cs), регистрируется в DI.
    /// </summary>
    public interface IQueryBus
    {
        /// <summary>
        /// Выполняет запрос и возвращает результат.
        /// </summary>
        /// <typeparam name="TResult">Тип результата запроса.</typeparam>
        /// <param name="query">Запрос (не может быть null).</param>
        /// <param name="context">Дополнительный контекст выполнения (может быть null).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Результат выполнения запроса.</returns>
        Task<TResult> QueryAsync<TResult>(
            IQuery<TResult> query,
            QueryContext? context = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Поведение конвейера обработки запросов (middleware).
    /// Позволяет добавить сквозную функциональность: логирование, авторизацию, кэширование и т.п.
    /// </summary>
    /// <remarks>
    /// В текущей реализации InMemoryBus конвейер пока не подключён.
    /// Интерфейс подготовлен для будущего использования.
    /// </remarks>
    public interface IQueryPipelineBehavior
    {
        /// <summary>
        /// Обрабатывает запрос в контексте конвейера.
        /// </summary>
        /// <typeparam name="TResult">Тип результата.</typeparam>
        /// <param name="query">Запрос.</param>
        /// <param name="context">Контекст выполнения.</param>
        /// <param name="next">Делегат, вызывающий следующий шаг конвейера или обработчик.</param>
        /// <returns>Задача с результатом.</returns>
        Task<TResult> HandleAsync<TResult>(
            IQuery<TResult> query,
            QueryContext context,
            Func<Task<TResult>> next);
    }

    /// <summary>
    /// Методы расширения для удобной работы с <see cref="IQueryBus"/>.
    /// </summary>
    public static class QueryBusExtensions
    {
        /// <summary>
        /// Выполняет запрос с явным токеном отмены.
        /// </summary>
        public static Task<TResult> QueryAsync<TResult>(
            this IQueryBus queryBus,
            IQuery<TResult> query,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(queryBus);
            ArgumentNullException.ThrowIfNull(query);

            return queryBus.QueryAsync(query, null, cancellationToken);
        }

        /// <summary>
        /// Выполняет запрос с указанием пользователя и токена отмены.
        /// </summary>
        public static Task<TResult> QueryAsync<TResult>(
            this IQueryBus queryBus,
            IQuery<TResult> query,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(queryBus);
            ArgumentNullException.ThrowIfNull(query);

            var context = new QueryContext
            {
                UserId = userId
            };
            return queryBus.QueryAsync(query, context, cancellationToken);
        }

        /// <summary>
        /// Выполняет запрос с указанием пользователя, игровой сессии и токена отмены.
        /// </summary>
        public static Task<TResult> QueryAsync<TResult>(
            this IQueryBus queryBus,
            IQuery<TResult> query,
            Guid userId,
            Guid gameSessionId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(queryBus);
            ArgumentNullException.ThrowIfNull(query);

            var context = new QueryContext
            {
                UserId = userId,
                GameSessionId = gameSessionId
            };
            return queryBus.QueryAsync(query, context, cancellationToken);
        }
    }
}