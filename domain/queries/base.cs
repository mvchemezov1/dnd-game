#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.domain.queries
{
    // --------------------------------------------------------------------------------------------
    // Базовые интерфейсы запросов
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Маркерный интерфейс запроса, возвращающего результат указанного типа.
    /// </summary>
    /// <typeparam name="TResult">Тип результата запроса.</typeparam>
    public interface IQuery<TResult>
    {
    }

    /// <summary>
    /// Обработчик запросов (CQRS Query Handler).
    /// </summary>
    /// <typeparam name="TQuery">Тип запроса, реализующий <see cref="IQuery{TResult}"/>.</typeparam>
    /// <typeparam name="TResult">Тип результата.</typeparam>
    public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
    {
        /// <summary>
        /// Выполняет запрос и возвращает результат.
        /// </summary>
        /// <param name="query">Запрос.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Результат выполнения запроса.</returns>
        Task<TResult> Handle(TQuery query, CancellationToken cancellationToken = default);
    }

    // --------------------------------------------------------------------------------------------
    // Контекст выполнения (игровая сессия, пользователь)
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Запрос, несущий контекст пользователя и игровой сессии.
    /// Позволяет автоматически проверять права доступа и логировать запросы.
    /// </summary>
    /// <typeparam name="TResult">Тип результата.</typeparam>
    public interface IGameQuery<TResult> : IQuery<TResult>
    {
        /// <summary>
        /// Идентификатор пользователя, выполняющего запрос.
        /// </summary>
        Guid UserId { get; init; }

        /// <summary>
        /// Идентификатор активной игровой сессии (кампании).
        /// </summary>
        Guid GameSessionId { get; init; }
    }

    // --------------------------------------------------------------------------------------------
    // Авторизация
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Запрос, требующий определённого разрешения для выполнения.
    /// </summary>
    /// <typeparam name="TResult">Тип результата.</typeparam>
    public interface IAuthorizedQuery<TResult> : IGameQuery<TResult>
    {
        /// <summary>
        /// Требуемое разрешение (например, "ViewCharacter", "EditCampaign").
        /// </summary>
        string RequiredPermission { get; }
    }

    // --------------------------------------------------------------------------------------------
    // Пагинация
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Запрос с поддержкой постраничного вывода.
    /// </summary>
    /// <typeparam name="TResult">Тип результата.</typeparam>
    public interface IPagedQuery<TResult> : IQuery<TResult>
    {
        /// <summary>Номер страницы (начиная с 1).</summary>
        int PageNumber { get; init; }

        /// <summary>Размер страницы (количество элементов на странице).</summary>
        int PageSize { get; init; }
    }

    /// <summary>
    /// Ответ с информацией о пагинации.
    /// </summary>
    /// <typeparam name="T">Тип элементов в списке.</typeparam>
    public class PagedResult<T>
    {
        /// <summary>Элементы текущей страницы.</summary>
        public List<T> Items { get; set; } = [];

        /// <summary>Общее количество элементов.</summary>
        public int TotalCount { get; set; }

        /// <summary>Номер текущей страницы.</summary>
        public int PageNumber { get; set; }

        /// <summary>Размер страницы.</summary>
        public int PageSize { get; set; }

        /// <summary>Общее количество страниц (вычисляется автоматически).</summary>
        public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    // --------------------------------------------------------------------------------------------
    // Сортировка
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Запрос с поддержкой сортировки.
    /// </summary>
    /// <typeparam name="TResult">Тип результата.</typeparam>
    public interface ISortedQuery<TResult> : IQuery<TResult>
    {
        /// <summary>Поле, по которому выполняется сортировка.</summary>
        string SortBy { get; init; }

        /// <summary>Направление сортировки: <c>true</c> — по убыванию, <c>false</c> — по возрастанию.</summary>
        bool SortDescending { get; init; }
    }

    // --------------------------------------------------------------------------------------------
    // Фильтрация
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Запрос с поддержкой фильтрации по ключу-значению.
    /// Конкретные запросы могут раскрывать фильтры своими свойствами.
    /// </summary>
    /// <typeparam name="TResult">Тип результата.</typeparam>
    public interface IFilteredQuery<TResult> : IQuery<TResult>
    {
        /// <summary>
        /// Набор фильтров в виде пар «ключ-значение».
        /// </summary>
        Dictionary<string, string> Filters { get; init; }
    }

    // --------------------------------------------------------------------------------------------
    // Базовый абстрактный класс запроса
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Удобная база для всех запросов с предустановленными свойствами контекста,
    /// пагинации и сортировки.
    /// </summary>
    /// <typeparam name="TResult">Тип результата запроса.</typeparam>
    public abstract record BaseQuery<TResult> : IGameQuery<TResult>, IPagedQuery<TResult>, ISortedQuery<TResult>
    {
        /// <inheritdoc/>
        public Guid UserId { get; init; }

        /// <inheritdoc/>
        public Guid GameSessionId { get; init; }

        /// <inheritdoc/>
        public int PageNumber { get; init; } = 1;

        /// <inheritdoc/>
        public int PageSize { get; init; } = 50;

        /// <inheritdoc/>
        public string SortBy { get; init; } = string.Empty;

        /// <inheritdoc/>
        public bool SortDescending { get; init; }
    }
}