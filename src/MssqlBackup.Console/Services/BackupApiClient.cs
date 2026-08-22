using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class BackupApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BackupApiClient> _logger;

    public BackupApiClient(HttpClient httpClient, ILogger<BackupApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SendBackupRecordAsync(BackupRecordDto record)
    {
        try
        {
            _logger.LogInformation("Sending backup record to API: {DatabaseName}", record.DatabaseName);

            var response = await _httpClient.PostAsJsonAsync("/api/backuprecords", record);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Backup record sent successfully for database '{DatabaseName}'", record.DatabaseName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send backup record to API for database '{DatabaseName}'", record.DatabaseName);
        }
    }
}
