using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MssqlBackup.Web.Models;

namespace MssqlBackup.Web.Services;

public class BackupApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BackupApiService> _logger;

    public BackupApiService(HttpClient httpClient, ILogger<BackupApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string HubUrl => $"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}/hubs/backup";

    public async Task<List<BackupRecordDto>> GetRecordsAsync(BackupFilter? filter = null)
    {
        try
        {
            var query = BuildQueryString(filter);
            var url = $"/api/backuprecords{query}";
            return await _httpClient.GetFromJsonAsync<List<BackupRecordDto>>(url) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch backup records");
            return [];
        }
    }

    public async Task<List<BackupRecordDto>> GetLatestAsync(string? environment = null)
    {
        try
        {
            var url = string.IsNullOrEmpty(environment)
                ? "/api/backuprecords/latest"
                : $"/api/backuprecords/latest?environment={Uri.EscapeDataString(environment)}";
            return await _httpClient.GetFromJsonAsync<List<BackupRecordDto>>(url) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch latest backup records");
            return [];
        }
    }

    public async Task<List<string>> GetEnvironmentsAsync()
    {
        try
        {
            var records = await _httpClient.GetFromJsonAsync<List<BackupRecordDto>>("/api/backuprecords") ?? [];
            return records.Select(r => r.EnvironmentName).Distinct().OrderBy(e => e).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch environments");
            return [];
        }
    }

    public async Task<List<string>> GetInstancesAsync()
    {
        try
        {
            var records = await _httpClient.GetFromJsonAsync<List<BackupRecordDto>>("/api/backuprecords") ?? [];
            return records.Select(r => r.InstanceName).Distinct().OrderBy(i => i).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch instances");
            return [];
        }
    }

    private static string BuildQueryString(BackupFilter? filter)
    {
        if (filter == null) return string.Empty;

        var parts = new List<string>();

        if (!string.IsNullOrEmpty(filter.Environment))
            parts.Add($"environment={Uri.EscapeDataString(filter.Environment)}");

        if (!string.IsNullOrEmpty(filter.Instance))
            parts.Add($"instance={Uri.EscapeDataString(filter.Instance)}");

        if (!string.IsNullOrEmpty(filter.Database))
            parts.Add($"database={Uri.EscapeDataString(filter.Database)}");

        if (filter.From.HasValue)
            parts.Add($"from={filter.From.Value:yyyy-MM-dd}");

        if (filter.To.HasValue)
            parts.Add($"to={filter.To.Value:yyyy-MM-dd}");

        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }
}
