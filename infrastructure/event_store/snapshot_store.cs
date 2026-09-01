#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using dnd_game.domain.aggregates;

namespace dnd_game.infrastructure.event_store
{
    /// <summary>
    /// Политика создания снимков.
    /// </summary>
    public enum SnapshotPolicy
    {
        /// <summary>Создавать снимок каждые N событий.</summary>
        EventCount,

        /// <summary>Создавать снимок через заданный интервал времени.</summary>
        TimeInterval,

        /// <summary>Не создавать снимки автоматически.</summary>
        Manual
    }

    /// <summary>
    /// Конфигурация создания снимков.
    /// </summary>
    public class SnapshotConfiguration
    {
        /// <summary>Политика создания снимков.</summary>
        public SnapshotPolicy Policy { get; set; } = SnapshotPolicy.EventCount;

        /// <summary>Интервал по количеству событий (используется при <see cref="SnapshotPolicy.EventCount"/>).</summary>
        public int EventCountInterval { get; set; } = 100;

        /// <summary>Интервал по времени (используется при <see cref="SnapshotPolicy.TimeInterval"/>).</summary>
        public TimeSpan TimeInterval { get; set; } = TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Интерфейс хранилища снимков.
    /// </summary>
    public interface ISnapshotStore
    {
        /// <summary>
        /// Получает последний снимок агрегата, версия которого не превышает заданную.
        /// </summary>
        Task<Snapshot?> GetLatestSnapshotAsync(Guid aggregateId, int maxVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// Сохраняет снимок агрегата.
        /// </summary>
        Task SaveSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет снимки старше указанной версии.
        /// </summary>
        Task DeleteSnapshotsOlderThanAsync(Guid aggregateId, int minVersionToKeep, CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет, нужно ли создать новый снимок для агрегата на основе политики.
        /// </summary>
        Task<bool> ShouldCreateSnapshotAsync(Guid aggregateId, int currentVersion, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Реализация хранилища снимков на основе PostgreSQL.
    /// Включает механизм сериализации агрегата и политику создания.
    /// </summary>
    public class SnapshotStore(string connectionString, SnapshotConfiguration config) : ISnapshotStore
    {
        private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        private readonly SnapshotConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private static readonly JsonSerializerSettings SnapshotJsonSettings = new()
        {
            ContractResolver = new PrivateMemberContractResolver(),
            TypeNameHandling = TypeNameHandling.Auto,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        /// <inheritdoc />
        public async Task<Snapshot?> GetLatestSnapshotAsync(
            Guid aggregateId,
            int maxVersion,
            CancellationToken cancellationToken = default)
        {
            if (aggregateId == Guid.Empty)
                throw new ArgumentException("Идентификатор агрегата не может быть пустым.", nameof(aggregateId));
            cancellationToken.ThrowIfCancellationRequested();

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = new NpgsqlCommand(@"
                SELECT version, data, created_at
                FROM snapshots
                WHERE aggregate_id = @aggId AND version <= @maxVer
                ORDER BY version DESC LIMIT 1
            ", conn);
            cmd.Parameters.AddWithValue("aggId", NpgsqlDbType.Uuid, aggregateId);
            cmd.Parameters.AddWithValue("maxVer", maxVersion);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new Snapshot
                {
                    AggregateId = aggregateId,
                    Version = reader.GetInt32(0),
                    Data = (byte[])reader[1],
                    CreatedAt = reader.GetDateTime(2)
                };
            }
            return null;
        }

        /// <inheritdoc />
        public async Task SaveSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            cancellationToken.ThrowIfCancellationRequested();

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO snapshots (aggregate_id, version, data, created_at)
                VALUES (@aggId, @ver, @data, @ts)
                ON CONFLICT (aggregate_id, version) DO UPDATE SET data = @data, created_at = @ts
            ", conn);
            cmd.Parameters.AddWithValue("aggId", NpgsqlDbType.Uuid, snapshot.AggregateId);
            cmd.Parameters.AddWithValue("ver", snapshot.Version);
            cmd.Parameters.AddWithValue("data", snapshot.Data);
            cmd.Parameters.AddWithValue("ts", snapshot.CreatedAt);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task DeleteSnapshotsOlderThanAsync(
            Guid aggregateId,
            int minVersionToKeep,
            CancellationToken cancellationToken = default)
        {
            // Опционально: можно реализовать удаление старых снимков для экономии места.
            // В текущей версии не требуется.
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<bool> ShouldCreateSnapshotAsync(
            Guid aggregateId,
            int currentVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _config.Policy switch
            {
                SnapshotPolicy.Manual => false,
                SnapshotPolicy.TimeInterval => await ShouldCreateByTimeAsync(aggregateId, cancellationToken),
                SnapshotPolicy.EventCount => await ShouldCreateByCountAsync(aggregateId, currentVersion, cancellationToken),
                _ => false
            };
        }

        // ---------- Статические методы сериализации ----------

        /// <summary>
        /// Создаёт снимок из агрегата, сериализуя его состояние.
        /// </summary>
        public static Snapshot CreateSnapshotFromAggregate(AggregateRoot aggregate)
        {
            ArgumentNullException.ThrowIfNull(aggregate);

            var json = JsonConvert.SerializeObject(aggregate, aggregate.GetType(), SnapshotJsonSettings);
            var data = Encoding.UTF8.GetBytes(json);
            return new Snapshot
            {
                AggregateId = aggregate.Id,
                Version = aggregate.Version,
                Data = data,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Восстанавливает агрегат из снимка.
        /// </summary>
        public static T? RestoreAggregateFromSnapshot<T>(Snapshot snapshot) where T : AggregateRoot, new()
        {
            if (snapshot?.Data == null || snapshot.Data.Length == 0)
                return null;

            var json = Encoding.UTF8.GetString(snapshot.Data);
            var aggregate = JsonConvert.DeserializeObject<T>(json, SnapshotJsonSettings);
            aggregate?.SetVersion(snapshot.Version);
            return aggregate;
        }

        // ---------- Вспомогательные методы ----------

        private async Task<bool> ShouldCreateByCountAsync(Guid aggregateId, int currentVersion, CancellationToken ct)
        {
            var latest = await GetLatestSnapshotAsync(aggregateId, int.MaxValue, ct);
            int lastVersion = latest?.Version ?? 0;
            return (currentVersion - lastVersion) >= _config.EventCountInterval;
        }

        private async Task<bool> ShouldCreateByTimeAsync(Guid aggregateId, CancellationToken ct)
        {
            var latest = await GetLatestSnapshotAsync(aggregateId, int.MaxValue, ct);
            if (latest == null)
                return true; // создать первый снимок
            return (DateTime.UtcNow - latest.CreatedAt) >= _config.TimeInterval;
        }

        /// <summary>
        /// Контракт-резолвер, позволяющий сериализовать приватные свойства и поля.
        /// </summary>
        private class PrivateMemberContractResolver : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var members = objectType.GetFields(flags).Cast<MemberInfo>()
                    .Concat(objectType.GetProperties(flags))
                    .Where(m => !m.IsDefined(typeof(JsonIgnoreAttribute), true))
                    .ToList();
                return members;
            }

            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var props = base.CreateProperties(type, memberSerialization);
                foreach (var prop in props)
                {
                    if (prop.Writable) continue;
                    var property = type.GetProperty(prop.UnderlyingName!,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property?.GetSetMethod(true) != null)
                    {
                        prop.Writable = true;
                        prop.ValueProvider = new ReflectionValueProvider(property);
                    }
                }
                return props;
            }
        }
    }
}