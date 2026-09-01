#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.caching
{
    /// <summary>
    /// Провайдер кэширования. Предоставляет асинхронные методы для чтения, записи и удаления данных из кэша.
    /// Реализации должны быть потокобезопасными и корректно обрабатывать отмену операций.
    /// </summary>
    public interface ICacheProvider
    {
        /// <summary>
        /// Получает объект из кэша по ключу.
        /// </summary>
        /// <typeparam name="T">Тип кэшируемого объекта (ссылочный тип).</typeparam>
        /// <param name="key">Ключ записи. Не должен быть пустым или состоять только из пробелов.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Объект из кэша или <c>null</c>, если запись отсутствует или истекла.</returns>
        /// <exception cref="ArgumentException">Если <paramref name="key"/> пуст или содержит только пробелы.</exception>
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Сохраняет объект в кэше.
        /// Если запись с таким ключом уже существует, она перезаписывается.
        /// </summary>
        /// <typeparam name="T">Тип кэшируемого объекта (ссылочный тип).</typeparam>
        /// <param name="key">Ключ записи. Не должен быть пустым или состоять только из пробелов.</param>
        /// <param name="value">Объект для сохранения. Не должен быть <c>null</c>.</param>
        /// <param name="expiry">
        /// Время жизни записи. Если <c>null</c> — запись хранится до явного удаления.
        /// Если задано значение меньше или равное <see cref="TimeSpan.Zero"/>, оно игнорируется (запись не имеет срока действия).
        /// </param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        /// <exception cref="ArgumentException">Если <paramref name="key"/> пуст или содержит только пробелы.</exception>
        /// <exception cref="ArgumentNullException">Если <paramref name="value"/> равен <c>null</c>.</exception>
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Удаляет запись из кэша по ключу.
        /// Если запись отсутствует, операция не вызывает ошибку.
        /// </summary>
        /// <param name="key">Ключ удаляемой записи. Не должен быть пустым или состоять только из пробелов.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        /// <exception cref="ArgumentException">Если <paramref name="key"/> пуст или содержит только пробелы.</exception>
        Task RemoveAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет, существует ли запись с указанным ключом и не истекла ли она.
        /// </summary>
        /// <param name="key">Ключ проверяемой записи. Не должен быть пустым или состоять только из пробелов.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <returns><c>true</c>, если запись существует и актуальна; иначе <c>false</c>.</returns>
        /// <exception cref="ArgumentException">Если <paramref name="key"/> пуст или содержит только пробелы.</exception>
        Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

        void RemoveSync(string key); // для использования внутри синхронных методов
    }
}