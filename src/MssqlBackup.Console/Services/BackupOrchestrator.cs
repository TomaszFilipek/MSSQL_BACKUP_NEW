using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class BackupOrchestrator
{
    private readonly BackupService _backupService;
    private readonly BackupApiClient _apiClient;
    private readonly ILogger<BackupOrchestrator> _logger;

    public BackupOrchestrator(BackupService backupService, BackupApiClient apiClient, ILogger<BackupOrchestrator> logger)
    {
        _backupService = backupService;
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<BackupResult> BackupAllDatabasesAsync(ServerConnection server, BackupConfiguration config, string environmentName)
    {
        _logger.LogInformation("Starting backup of all databases on server '{Server}'", server.Server);

        var allDatabases = await _backupService.GetDatabasesAsync(server);
        _logger.LogInformation("Found {Count} databases on server", allDatabases.Count);

        var databasesToBackup = FilterDatabases(allDatabases, config.ExcludeDatabases);
        _logger.LogInformation("After filtering: {Count} databases to backup", databasesToBackup.Count);

        var result = new BackupResult { TotalDatabases = databasesToBackup.Count };

        foreach (var database in databasesToBackup)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var outputPath = BuildOutputPath(config.OutputDirectory, database);
                var directory = Path.GetDirectoryName(outputPath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new BackupOptions
                {
                    DatabaseName = database,
                    OutputPath = outputPath,
                    Type = config.DefaultType,
                    Compress = config.Compress,
                    Verify = config.Verify
                };

                await _backupService.BackupAsync(server, options);
                stopwatch.Stop();
                result.SuccessfulBackups++;

                _logger.LogInformation("Backup of database '{Database}' completed successfully", database);

                var fileInfo = new FileInfo(outputPath);
                var record = new BackupRecordDto
                {
                    EnvironmentName = environmentName,
                    InstanceName = server.Server,
                    DatabaseName = database,
                    BackupType = config.DefaultType.ToString(),
                    OutputFilePath = outputPath,
                    FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                    BackupDate = DateTime.Now,
                    Compress = config.Compress,
                    Verify = config.Verify,
                    Duration = stopwatch.Elapsed
                };

                await _apiClient.SendBackupRecordAsync(record);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.FailedBackups++;
                result.Errors.Add(new BackupError
                {
                    DatabaseName = database,
                    ErrorMessage = ex.Message
                });

                _logger.LogError(ex, "Backup of database '{Database}' failed", database);
            }
        }

        _logger.LogInformation("Backup completed: {Successful} successful, {Failed} failed",
            result.SuccessfulBackups, result.FailedBackups);

        return result;
    }

    public static string BuildOutputPath(string outputDirectory, string databaseName)
    {
        var dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
        var fileName = $"{databaseName}.bak";
        return Path.Combine(outputDirectory, dateFolder, fileName);
    }

    public static List<string> FilterDatabases(List<string> allDatabases, List<string> excludeDatabases)
    {
        return allDatabases
            .Where(db => !excludeDatabases.Contains(db, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
