using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class BackupJobApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BackupJobApiClient> _logger;

    public BackupJobApiClient(HttpClient httpClient, ILogger<BackupJobApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Guid?> CreateJobAsync(BackupJobDto job)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/backupjobs", job);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<BackupJobDto>();
            _logger.LogDebug("Backup job created {JobId}", created?.Id);
            return created?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create backup job on API");
            return null;
        }
    }

    public async Task UpdateJobAsync(BackupJobDto job)
    {
        try
        {
            job.UpdatedAt = DateTime.UtcNow;
            var response = await _httpClient.PutAsJsonAsync($"/api/backupjobs/{job.Id}", job);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update backup job {JobId}", job.Id);
        }
    }
}
