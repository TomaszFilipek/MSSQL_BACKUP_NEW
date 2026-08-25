using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class BackupOrchestrator
{
    private readonly BackupService _backupService;
    private readonly BackupApiClient _apiClient;
    private readonly CompressionService _compressionService;
    private readonly SambaService _sambaService;
    private readonly ILogger<BackupOrchestrator> _logger;

    public BackupOrchestrator(
        BackupService backupService,
        BackupApiClient apiClient,
        CompressionService compressionService,
        SambaService sambaService,
        ILogger<BackupOrchestrator> logger)
    {
        _backupService = backupService;
        _apiClient = apiClient;
        _compressionService = compressionService;
        _sambaService = sambaService;
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
                var outputPath = BuildOutputPath(config.OutputDirectory, server.Server, database);
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
                _logger.LogInformation("Backup of database '{Database}' completed successfully", database);

                var finalFilePath = outputPath;
                long fileSizeBeforeCompression = new FileInfo(outputPath).Length;
                long fileSizeAfterCompression = fileSizeBeforeCompression;

                if (config.PostBackupCompression.Compress)
                {
                    await _compressionService.CompressFileAsync(outputPath, config.PostBackupCompression);
                    finalFilePath = outputPath + ".7z";
                    fileSizeAfterCompression = new FileInfo(finalFilePath).Length;
                    _logger.LogInformation("Compression of backup '{Database}' completed successfully. Before: {Before} bytes, After: {After} bytes",
                        database, fileSizeBeforeCompression, fileSizeAfterCompression);
                }

                if (config.Samba.Enabled)
                {
                    _logger.LogInformation("Copying backup '{Database}' to Samba share", database);
                    await _sambaService.CopyToShareAsync(finalFilePath, config.Samba);

                    var shareFileName = Path.GetFileName(finalFilePath);
                    finalFilePath = Path.Combine(config.Samba.SharePath, shareFileName);
                }

                stopwatch.Stop();

                var record = new BackupRecordDto
                {
                    EnvironmentName = environmentName,
                    InstanceName = server.Server,
                    DatabaseName = database,
                    BackupType = config.DefaultType.ToString(),
                    OutputFilePath = finalFilePath,
                    FileSize = fileSizeAfterCompression,
                    FileSizeBeforeCompression = fileSizeBeforeCompression,
                    FileSizeAfterCompression = fileSizeAfterCompression,
                    BackupDate = DateTime.Now,
                    Compress = config.Compress || config.PostBackupCompression.Compress,
                    Verify = config.Verify,
                    Duration = stopwatch.Elapsed
                };

                if (config.SendToApi)
                {
                    await _apiClient.SendBackupRecordAsync(record);
                }
                else
                {
                    _logger.LogInformation("SendToApi disabled - skipping API call for '{Database}'", database);
                }

                result.SuccessfulBackups++;
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

    public static string BuildOutputPath(string outputDirectory, string instanceName, string databaseName)
    {
        var dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{databaseName}_{timestamp}.bak";
        return Path.Combine(outputDirectory, instanceName, dateFolder, fileName);
    }

    public static List<string> FilterDatabases(List<string> allDatabases, List<string> excludeDatabases)
    {
        return allDatabases
            .Where(db => !excludeDatabases.Contains(db, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
