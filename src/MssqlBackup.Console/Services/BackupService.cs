using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class BackupService
{
    private readonly ILogger<BackupService> _logger;

    public BackupService(ILogger<BackupService> logger)
    {
        _logger = logger;
    }

    public async Task BackupAsync(ServerConnection server, BackupOptions options)
    {
        var connectionString = BuildConnectionString(server);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var isExpress = await IsExpressEditionAsync(connection);
        var compressEnabled = options.Compress && !isExpress;

        if (isExpress && options.Compress)
        {
            _logger.LogWarning("SQL Server Express Edition detected - BACKUP WITH COMPRESSION is not supported. Skipping SQL compression.");
        }

        var command = BuildBackupCommand(new BackupOptions
        {
            DatabaseName = options.DatabaseName,
            OutputPath = options.OutputPath,
            Type = options.Type,
            Compress = compressEnabled,
            Verify = options.Verify
        });

        _logger.LogInformation("Starting {BackupType} backup of database '{DatabaseName}' to '{OutputPath}'",
            options.Type, options.DatabaseName, options.OutputPath);

        await using var cmd = new SqlCommand(command, connection);
        cmd.CommandTimeout = 0;

        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Backup completed successfully");

        if (options.Verify)
        {
            _logger.LogInformation("Verifying backup...");
            var verifyCommand = $"RESTORE VERIFYONLY FROM DISK = N'{options.OutputPath}'";

            await using var verifyCmd = new SqlCommand(verifyCommand, connection);
            await verifyCmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Backup verified successfully");
        }
    }

    public async Task<List<string>> GetDatabasesAsync(ServerConnection server)
    {
        var connectionString = BuildConnectionString(server);
        var databases = new List<string>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var command = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name";

        await using var cmd = new SqlCommand(command, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    private static async Task<bool> IsExpressEditionAsync(SqlConnection connection)
    {
        const string query = "SELECT SERVERPROPERTY('ProductVersion')";
        await using var cmd = new SqlCommand(query, connection);
        var version = (await cmd.ExecuteScalarAsync())?.ToString() ?? string.Empty;

        const string editionQuery = "SELECT SERVERPROPERTY('Edition')";
        await using var editionCmd = new SqlCommand(editionQuery, connection);
        var edition = (await editionCmd.ExecuteScalarAsync())?.ToString() ?? string.Empty;

        return edition.Contains("Express", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildConnectionString(ServerConnection server)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server.Server,
            InitialCatalog = server.Database ?? "master",
            TrustServerCertificate = true
        };

        if (server.UseWindowsAuth)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.IntegratedSecurity = false;
            builder.UserID = server.Username ?? string.Empty;
            builder.Password = server.Password ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    private static string BuildBackupCommand(BackupOptions options)
    {
        var withClause = new List<string> { "FORMAT" };

        if (options.Compress)
            withClause.Add("COMPRESSION");

        if (options.Type == BackupType.Differential)
            withClause.Add("DIFFERENTIAL");

        var with = string.Join(", ", withClause);

        return $"""
            BACKUP DATABASE [{options.DatabaseName}] 
            TO DISK = N'{options.OutputPath}' 
            WITH {with}
            """;
    }
}
