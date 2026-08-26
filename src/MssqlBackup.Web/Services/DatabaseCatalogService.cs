using System.Net.Http.Json;
using MssqlBackup.Web.Models;

namespace MssqlBackup.Web.Services;

public class DatabaseCatalogService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DatabaseCatalogService> _logger;

    public DatabaseCatalogService(HttpClient httpClient, ILogger<DatabaseCatalogService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<DatabaseCatalogDto>> GetDatabasesAsync(DatabaseCatalogFilter? filter = null, string? sortBy = null, bool desc = false, int take = 500)
    {
        try
        {
            var query = BuildQuery(filter, sortBy, desc, take);
            return await _httpClient.GetFromJsonAsync<List<DatabaseCatalogDto>>($"/api/databases{query}") ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch database catalog");
            return [];
        }
    }

    private static string BuildQuery(DatabaseCatalogFilter? filter, string? sortBy, bool desc, int take)
    {
        var parts = new List<string> { $"take={take}" };
        if (filter != null)
        {
            if (!string.IsNullOrEmpty(filter.Environment))
                parts.Add($"environment={Uri.EscapeDataString(filter.Environment)}");
            if (!string.IsNullOrEmpty(filter.Server))
                parts.Add($"server={Uri.EscapeDataString(filter.Server)}");
            if (!string.IsNullOrEmpty(filter.Instance))
                parts.Add($"instance={Uri.EscapeDataString(filter.Instance)}");
            if (!string.IsNullOrEmpty(filter.Database))
                parts.Add($"database={Uri.EscapeDataString(filter.Database)}");
            if (filter.IsActive.HasValue)
                parts.Add($"isActive={filter.IsActive.Value.ToString().ToLower()}");
        }
        if (!string.IsNullOrEmpty(sortBy))
        {
            parts.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
            parts.Add($"desc={desc.ToString().ToLower()}");
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }
}
