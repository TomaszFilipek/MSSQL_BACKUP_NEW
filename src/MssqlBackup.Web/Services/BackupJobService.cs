using System.Net.Http.Json;
using MssqlBackup.Web.Models;

namespace MssqlBackup.Web.Services;

public class BackupJobService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BackupJobService> _logger;

    public BackupJobService(HttpClient httpClient, ILogger<BackupJobService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string HubUrl => $"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}/hubs/backup";

    public async Task<List<BackupJobDto>> GetActiveAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<BackupJobDto>>("/api/backupjobs/active") ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch active jobs");
            return [];
        }
    }

    public async Task<List<BackupJobDto>> GetRecentAsync(int take = 50)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<BackupJobDto>>($"/api/backupjobs?take={take}") ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch recent jobs");
            return [];
        }
    }

    public async Task<List<BackupJobDto>> GetAllAsync(string? environment = null, string? status = null, int take = 50)
    {
        try
        {
            var url = $"/api/backupjobs?take={take}";
            if (!string.IsNullOrEmpty(environment))
                url += $"&environment={Uri.EscapeDataString(environment)}";
            if (!string.IsNullOrEmpty(status))
                url += $"&status={Uri.EscapeDataString(status)}";
            return await _httpClient.GetFromJsonAsync<List<BackupJobDto>>(url) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch jobs");
            return [];
        }
    }
}
