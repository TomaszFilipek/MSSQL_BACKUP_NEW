using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class BackupOrchestrator
{
    private readonly BackupService _backupService;
    private readonly BackupApiClient _apiClient;
    private readonly BackupJobApiClient _jobClient;
    private readonly CompressionService _compressionService;
    private readonly SambaService _sambaService;
    private readonly ILogger<BackupOrchestrator> _logger;

    public BackupOrchestrator(
        BackupService backupService,
        BackupApiClient apiClient,
        BackupJobApiClient jobClient,
        CompressionService compressionService,
        SambaService sambaService,
        ILogger<BackupOrchestrator> logger)
    {
        _backupService = backupService;
        _apiClient = apiClient;
        _jobClient = jobClient;
        _compressionService = compressionService;
        _sambaService = sambaService;
        _logger = logger;
    }

    public async Task<BackupResult> BackupAllDatabasesAsync(ServerConnection server, BackupConfiguration config, string environmentName)
    {
        _logger.LogInformation("Starting backup of all databases on server '{Server}'", server.Server);

        var warsawZone = GetWarsawZone();
        var warsawJobTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, warsawZone);
        var jobTimestampFolder = warsawJobTime.ToString("yyyy-MM-dd HH-mm-ss");

        BackupJobDto? job = null;
        try
        {
            // create job for live panel (fire-and-forget errors)
            job = new BackupJobDto
            {
                Id = Guid.NewGuid(),
                EnvironmentName = environmentName,
                InstanceName = server.Server,
                HostName = Environment.MachineName,
                Status = "Running",
                StartedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TotalDatabases = 0,
                CompletedCount = 0,
                FailedCount = 0,
                CurrentStep = "Listing databases",
                Message = $"Rozpoczynam backup na {server.Server}"
            };
            await _jobClient.CreateJobAsync(job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create job on API - continuing without live reporting");
        }

        List<string> allDatabases;
        try
        {
            allDatabases = await _backupService.GetDatabasesAsync(server);
            _logger.LogInformation("Found {Count} databases on server", allDatabases.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list databases");
            if (job != null)
            {
                job.Status = "Failed";
                job.FinishedAt = DateTime.UtcNow;
                job.CurrentStep = "Failed";
                job.Message = $"Blad listowania baz: {ex.Message}";
                await _jobClient.UpdateJobAsync(job);
            }
            return new BackupResult { TotalDatabases = 0, FailedBackups = 1, Errors = [new BackupError { DatabaseName = "*", ErrorMessage = ex.Message }] };
        }

        var databasesToBackup = FilterDatabases(allDatabases, config.ExcludeDatabases);
        _logger.LogInformation("After filtering: {Count} databases to backup", databasesToBackup.Count);

        if (job != null)
        {
            job.TotalDatabases = databasesToBackup.Count;
            job.CurrentStep = databasesToBackup.Count == 0 ? "Idle - no databases" : "Starting";
            job.Message = $"Do zbackupowania: {databasesToBackup.Count} baz";
            await _jobClient.UpdateJobAsync(job);
        }

        var result = new BackupResult { TotalDatabases = databasesToBackup.Count };

        for (int i = 0; i < databasesToBackup.Count; i++)
        {
            var database = databasesToBackup[i];
            var stopwatch = Stopwatch.StartNew();

            if (job != null)
            {
                job.CurrentDatabase = database;
                job.CurrentStep = "Backup";
                job.Message = $"Backup {database} ({i + 1}/{databasesToBackup.Count})";
                job.UpdatedAt = DateTime.UtcNow;
                await _jobClient.UpdateJobAsync(job);
            }

            try
            {
                var outputPath = BuildOutputPath(config.OutputDirectory, environmentName, warsawJobTime, database);
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
                    if (job != null)
                    {
                        job.CurrentStep = "Compressing";
                        job.Message = $"Kompresja {database}";
                        await _jobClient.UpdateJobAsync(job);
                    }
                    await _compressionService.CompressFileAsync(outputPath, config.PostBackupCompression);
                    finalFilePath = outputPath + ".7z";
                    fileSizeAfterCompression = new FileInfo(finalFilePath).Length;
                    _logger.LogInformation("Compression of backup '{Database}' completed successfully. Before: {Before} bytes, After: {After} bytes",
                        database, fileSizeBeforeCompression, fileSizeAfterCompression);

                    if (config.PostBackupCompression.DeleteSourceAfterCompress && File.Exists(outputPath))
                    {
                        try
                        {
                            File.Delete(outputPath);
                            _logger.LogInformation("Deleted source file after compression: {File}", outputPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete source file {File}", outputPath);
                        }
                    }
                }

                if (config.Samba.Enabled)
                {
                    if (job != null)
                    {
                        job.CurrentStep = "Copying to Samba";
                        job.Message = $"Kopiowanie {database} na Samba";
                        await _jobClient.UpdateJobAsync(job);
                    }
                    _logger.LogInformation("Copying backup '{Database}' to Samba share", database);
                    var sambaFolder = Path.Combine(config.Samba.SharePath, environmentName, jobTimestampFolder);
                    var sambaDestPath = Path.Combine(sambaFolder, Path.GetFileName(finalFilePath));
                    await _sambaService.CopyToShareAsync(finalFilePath, sambaDestPath, config.Samba);
                    finalFilePath = sambaDestPath;
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
                    BackupDate = DateTime.UtcNow,
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
                if (job != null)
                {
                    job.CompletedCount = result.SuccessfulBackups;
                    job.FailedCount = result.FailedBackups;
                    job.CurrentStep = "Done";
                    job.Message = $"Zakonczono {database} OK";
                    await _jobClient.UpdateJobAsync(job);
                }
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
                if (job != null)
                {
                    job.FailedCount = result.FailedBackups;
                    job.CompletedCount = result.SuccessfulBackups;
                    job.CurrentStep = "Error";
                    job.Message = $"Blad {database}: {ex.Message}";
                    await _jobClient.UpdateJobAsync(job);
                }
            }
        }

        if (job != null)
        {
            job.Status = result.FailedBackups == 0 ? "Completed" : (result.SuccessfulBackups == 0 ? "Failed" : "CompletedWithErrors");
            job.FinishedAt = DateTime.UtcNow;
            job.CurrentDatabase = null;
            job.CurrentStep = job.Status;
            job.Message = $"Zakonczono: {result.SuccessfulBackups} OK, {result.FailedBackups} bledow";
            job.UpdatedAt = DateTime.UtcNow;
            await _jobClient.UpdateJobAsync(job);
        }

        _logger.LogInformation("Backup completed: {Successful} successful, {Failed} failed",
            result.SuccessfulBackups, result.FailedBackups);

        return result;
    }

    public static string BuildOutputPath(string outputDirectory, string environmentName, DateTime warsawJobTime, string databaseName)
    {
        var folder = warsawJobTime.ToString("yyyy-MM-dd HH-mm-ss");
        var timestamp = warsawJobTime.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{databaseName}_{timestamp}.bak";
        return Path.Combine(outputDirectory, environmentName, folder, fileName);
    }

    // Backward compatible overload (for tests)
    public static string BuildOutputPath(string outputDirectory, string instanceName, string databaseName)
    {
        var warsawZone = GetWarsawZone();
        var warsawNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, warsawZone);
        return BuildOutputPath(outputDirectory, instanceName, warsawNow, databaseName);
    }

    private static TimeZoneInfo GetWarsawZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch { try { return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"); } catch { return TimeZoneInfo.CreateCustomTimeZone("CEST", TimeSpan.FromHours(2), "CEST", "CEST"); } }
    }

    public static List<string> FilterDatabases(List<string> allDatabases, List<string> excludeDatabases)
    {
        return allDatabases
            .Where(db => !excludeDatabases.Contains(db, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
