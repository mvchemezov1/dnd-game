using DbUp;

namespace dnd_game.migrations;

public class DatabaseMigrator
{
    private readonly string _connectionString;
    private readonly string _migrationsPath;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(
        string connectionString,
        string migrationsPath,
        ILogger<DatabaseMigrator> logger)
    {
        _connectionString = connectionString;
        _migrationsPath = migrationsPath;
        _logger = logger;
    }

    public bool Migrate()
    {
        try
        {
            EnsureDatabase.For.PostgresqlDatabase(_connectionString);

            var upgrader = DeployChanges.To
                .PostgresqlDatabase(_connectionString)
                .WithScriptsFromFileSystem(_migrationsPath)
                .WithTransaction()
                .LogToConsole()
                .Build();

            var result = upgrader.PerformUpgrade();

            if (!result.Successful)
            {
                _logger.LogError(result.Error, "Database migration failed.");
                return false;
            }

            _logger.LogInformation("Database migration completed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed with exception: {FullException}", ex.ToString());
            return false;
        }
    }
}