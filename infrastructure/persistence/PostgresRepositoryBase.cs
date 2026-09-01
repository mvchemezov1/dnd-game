#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    /// <summary>
    /// Базовый класс для всех PostgreSQL-репозиториев. Предоставляет
    /// общую логику открытия соединений и логирования.
    /// </summary>
    public abstract class PostgresRepositoryBase
    {
        protected string ConnectionString { get; }
        protected ILogger Logger { get; }

        protected PostgresRepositoryBase(string connectionString, ILogger logger)
        {
            ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Создаёт и открывает новое соединение с PostgreSQL.
        /// </summary>
        protected async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }

        /// <summary>
        /// Выполняет SQL-команду, не возвращающую результат.
        /// </summary>
        protected async Task ExecuteNonQueryAsync(string sql, Action<NpgsqlCommand>? configure = null, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            configure?.Invoke(command);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Выполняет SQL-команду и возвращает скалярное значение.
        /// </summary>
        protected async Task<object?> ExecuteScalarAsync(string sql, Action<NpgsqlCommand>? configure = null, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            configure?.Invoke(command);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}