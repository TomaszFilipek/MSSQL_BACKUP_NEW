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
    private readonly LocalCopyService _localCopyService;
    private readonly ILogger<BackupOrchestrator> _logger;

    public BackupOrchestrator(
        BackupService backupService,
        BackupApiClient apiClient,
        BackupJobApiClient jobClient,
        CompressionService compressionService,
        SambaService sambaService,
        LocalCopyService localCopyService,
        ILogger<BackupOrchestrator> logger)
    {
        _backupService = backupService;
        _apiClient = apiClient;
        _jobClient = jobClient;
        _compressionService = compressionService;
        _sambaService = sambaService;
        _localCopyService = localCopyService;
        _logger = logger;
    }

    public async Task<BackupResult> BackupAllDatabasesAsync(ServerConnection server, BackupConfiguration config, string environmentName, string? serverName = null, int totalServers = 1, int serverIndex = 1)
    {
        _logger.LogInformation("Starting backup of all databases on server '{Server}' ({Index}/{Total})", server.Server, serverIndex, totalServers);

        var warsawZone = GetWarsawZone();
        var warsawJobTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, warsawZone);
        var typeSuffix = config.DefaultType == BackupType.Differential ? "Diff" : "Full";
        var jobTimestampFolder = $"{warsawJobTime:yyyy-MM-dd HH-mm-ss}_{typeSuffix}";

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
                ServerName = serverName ?? server.Server,
                TotalServers = totalServers,
                ServerIndex = serverIndex,
                Status = "Running",
                BackupType = config.DefaultType.ToString(),
                StartedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TotalDatabases = 0,
                CompletedCount = 0,
                FailedCount = 0,
                CurrentStep = "Listing databases",
                Message = $"Rozpoczynam backup na {server.Server} ({serverIndex}/{totalServers})"
            };
            await _jobClient.CreateJobAsync(job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create job on API - continuing without live reporting");
        }

        try
        {
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
                    job.UpdatedAt = DateTime.UtcNow;
                    try { await _jobClient.UpdateJobAsync(job); } catch { }
                }
                return new BackupResult { TotalDatabases = 0, FailedBackups = 1, Errors = [new BackupError { DatabaseName = "*", ErrorMessage = ex.Message }] };
            }

        var databasesToBackup = FilterDatabases(allDatabases, config.ExcludeDatabases);
        _logger.LogInformation("After filtering: {Count} databases to backup", databasesToBackup.Count);

        if (job != null)
        {
            job.TotalDatabases = databasesToBackup.Count;
            job.Databases = databasesToBackup.Select(db => new BackupJobDatabaseInfo { DatabaseName = db, Status = "Pending" }).ToList();
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
                var dbEntry = job.Databases.FirstOrDefault(d => d.DatabaseName == database);
                if (dbEntry != null) dbEntry.Status = "Running";
                job.UpdatedAt = DateTime.UtcNow;
                await _jobClient.UpdateJobAsync(job);
            }

            try
            {
                var safeServer = SanitizeFileName(serverName ?? server.Server);
                var outputPath = BuildOutputPath(config.OutputDirectory, environmentName, safeServer, warsawJobTime, database, config.DefaultType);
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

                // Kopia do celow zewnetrznych - zawsze na koncu po kompresji
                var sambaEnabled = config.Samba.Enabled;
                var localEnabled = config.LocalCopy.Enabled && !string.IsNullOrWhiteSpace(config.LocalCopy.DestinationPath);
                var bothCopyEnabled = sambaEnabled && localEnabled;

                if (sambaEnabled || localEnabled)
                {
                    var sourceForCopy = finalFilePath;
                    string? sambaDestPath = null;
                    string? localDestPath = null;

                    if (sambaEnabled)
                    {
                        if (job != null)
                        {
                            job.CurrentStep = "Copying to Samba";
                            job.Message = $"Kopiowanie {database} na Samba";
                            await _jobClient.UpdateJobAsync(job);
                        }
                        _logger.LogInformation("Copying backup '{Database}' to Samba share", database);
                        var safeServerSamba = SanitizeFileName(serverName ?? server.Server);
                        var sambaFolder = Path.Combine(config.Samba.SharePath, environmentName, safeServerSamba, jobTimestampFolder);
                        sambaDestPath = Path.Combine(sambaFolder, Path.GetFileName(finalFilePath));
                        var sambaSettingsForCopy = bothCopyEnabled
                            ? new SambaSettings
                            {
                                Enabled = config.Samba.Enabled,
                                SharePath = config.Samba.SharePath,
                                Username = config.Samba.Username,
                                Password = config.Samba.Password,
                                Domain = config.Samba.Domain,
                                DeleteSourceAfterCopy = false,
                                CreateOkFile = config.Samba.CreateOkFile
                            }
                            : config.Samba;
                        await _sambaService.CopyToShareAsync(finalFilePath, sambaDestPath, sambaSettingsForCopy);
                    }

                    if (localEnabled)
                    {
                        if (job != null)
                        {
                            job.CurrentStep = sambaEnabled ? "Copying to local folder" : "Copying to local folder";
                            job.Message = $"Kopiowanie {database} do folderu lokalnego";
                            await _jobClient.UpdateJobAsync(job);
                        }
                        _logger.LogInformation("Copying backup '{Database}' to local folder", database);
                        var safeServerLocal = SanitizeFileName(serverName ?? server.Server);
                        var localFolder = Path.Combine(config.LocalCopy.DestinationPath, environmentName, safeServerLocal, jobTimestampFolder);
                        localDestPath = Path.Combine(localFolder, Path.GetFileName(sourceForCopy));
                        var localSettingsForCopy = bothCopyEnabled
                            ? new LocalCopySettings
                            {
                                Enabled = config.LocalCopy.Enabled,
                                DestinationPath = config.LocalCopy.DestinationPath,
                                DeleteSourceAfterCopy = false
                            }
                            : config.LocalCopy;
                        await _localCopyService.CopyAsync(sourceForCopy, localDestPath, localSettingsForCopy);
                    }

                    if (bothCopyEnabled && (config.Samba.DeleteSourceAfterCopy || config.LocalCopy.DeleteSourceAfterCopy))
                    {
                        try
                        {
                            if (File.Exists(sourceForCopy))
                            {
                                File.Delete(sourceForCopy);
                                _logger.LogInformation("Deleted source file after copies: {File}", sourceForCopy);
                                if (config.Samba.CreateOkFile)
                                {
                                    var okFile = sourceForCopy + ".ok";
                                    File.WriteAllText(okFile, $"Moved to Samba/LocalCopy: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete source file {File} after copies", sourceForCopy);
                        }
                    }

                    // Rekord wskazuje na docelowa lokalizacje (preferuj Samba gdy oba)
                    if (sambaDestPath != null) finalFilePath = sambaDestPath;
                    else if (localDestPath != null) finalFilePath = localDestPath;
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
                    var dbOk = job.Databases.FirstOrDefault(d => d.DatabaseName == database);
                    if (dbOk != null)
                    {
                        dbOk.Status = "Completed";
                        dbOk.FileSize = fileSizeAfterCompression;
                        dbOk.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
                    }
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
                    var dbErr = job.Databases.FirstOrDefault(d => d.DatabaseName == database);
                    if (dbErr != null)
                    {
                        dbErr.Status = "Failed";
                        dbErr.ErrorMessage = ex.Message;
                        dbErr.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
                    }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during backup of server {Server}", server.Server);
            if (job != null && job.Status == "Running")
            {
                try
                {
                    job.Status = "Failed";
                    job.FinishedAt = DateTime.UtcNow;
                    job.CurrentStep = "Error";
                    job.Message = $"Nieoczekiwany błąd: {ex.Message}";
                    job.UpdatedAt = DateTime.UtcNow;
                    await _jobClient.UpdateJobAsync(job);
                }
                catch (Exception updEx)
                {
                    _logger.LogWarning(updEx, "Failed to mark job as Failed");
                }
            }
            throw;
        }
    }

    public static string BuildOutputPath(string outputDirectory, string environmentName, string serverName, DateTime warsawJobTime, string databaseName, BackupType type = BackupType.Full)
    {
        var suffix = type == BackupType.Differential ? "Diff" : "Full";
        var folder = $"{warsawJobTime:yyyy-MM-dd HH-mm-ss}_{suffix}";
        var timestamp = warsawJobTime.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{databaseName}_{timestamp}.bak";
        var safeServer = SanitizeFileName(serverName);
        return Path.Combine(outputDirectory, environmentName, safeServer, folder, fileName);
    }

    public static string BuildOutputPath(string outputDirectory, string environmentName, DateTime warsawJobTime, string databaseName)
    {
        // fallback when serverName not provided (legacy) -> treat environmentName as serverName, default Full
        return BuildOutputPath(outputDirectory, environmentName, environmentName, warsawJobTime, databaseName, BackupType.Full);
    }

    // Backward compatible overload (for tests)
    public static string BuildOutputPath(string outputDirectory, string instanceName, string databaseName)
    {
        var warsawZone = GetWarsawZone();
        var warsawNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, warsawZone);
        return BuildOutputPath(outputDirectory, instanceName, instanceName, warsawNow, databaseName, BackupType.Full);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct().ToArray();
        var safe = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safe)) safe = "Default";
        // additionally replace characters not safe for SMB/Linux: \ / : * ? " < > |
        foreach (var c in new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
            safe = safe.Replace(c, '_');
        // replace ".\SQLEXPRESS" -> "_SQLEXPRESS"
        safe = safe.Replace(".", "_").Trim('_');
        if (safe.Length == 0) safe = "Default";
        return safe;
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
