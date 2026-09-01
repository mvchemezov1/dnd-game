#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// Фоновый сервис периодической очистки истёкших refresh-токенов.
    /// Предотвращает неограниченный рост таблицы refresh_tokens.
    /// </summary>
    public sealed class RefreshTokenCleanupService : BackgroundService
    {
        private readonly IRefreshTokenStore _refreshTokenStore;
        private readonly ILogger<RefreshTokenCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval;

        /// <summary>
        /// Создаёт экземпляр сервиса очистки.
        /// </summary>
        /// <param name="refreshTokenStore">Хранилище refresh-токенов.</param>
        /// <param name="logger">Логгер.</param>
        /// <param name="cleanupInterval">Интервал между очистками. По умолчанию 1 час.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="refreshTokenStore"/> или <paramref name="logger"/> равны null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="cleanupInterval"/> меньше или равен нулю.</exception>
        public RefreshTokenCleanupService(
            IRefreshTokenStore refreshTokenStore,
            ILogger<RefreshTokenCleanupService> logger,
            TimeSpan? cleanupInterval = null)
        {
            _refreshTokenStore = refreshTokenStore ?? throw new ArgumentNullException(nameof(refreshTokenStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cleanupInterval = cleanupInterval ?? TimeSpan.FromHours(1);

            if (_cleanupInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(cleanupInterval), "Интервал очистки должен быть положительным.");
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Сервис очистки refresh-токенов запущен. Интервал: {CleanupInterval}.", _cleanupInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Ожидаем заданный интервал перед следующей очисткой
                    await Task.Delay(_cleanupInterval, stoppingToken).ConfigureAwait(false);

                    // Выполняем очистку
                    int deleted = await _refreshTokenStore.DeleteExpiredAsync(stoppingToken).ConfigureAwait(false);
                    if (deleted > 0)
                    {
                        _logger.LogInformation("Удалено истёкших refresh-токенов: {DeletedCount}.", deleted);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Ожидаемая отмена при остановке сервиса
                    _logger.LogInformation("Остановка сервиса очистки refresh-токенов...");
                    break;
                }
                catch (Exception ex)
                {
                    // Логируем ошибку, но продолжаем работу — следующая попытка произойдёт по расписанию
                    _logger.LogError(ex, "Ошибка при очистке истёкших refresh-токенов.");
                }
            }

            _logger.LogInformation("Сервис очистки refresh-токенов остановлен.");
        }
    }
}