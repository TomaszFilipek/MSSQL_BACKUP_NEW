using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace MssqlBackup.Console.Services;

public class DatabaseCatalogApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DatabaseCatalogApiClient> _logger;

    public DatabaseCatalogApiClient(HttpClient httpClient, ILogger<DatabaseCatalogApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SyncAsync(string environment, string instance, string serverName, List<string> databases)
    {
        var payload = new
        {
            EnvironmentName = environment,
            InstanceName = instance,
            ServerName = serverName,
            DatabaseNames = databases
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/databases/sync", payload);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Synced {Count} databases for {Instance} ({ServerName})", databases.Count, instance, serverName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync databases for {Instance}", instance);
            throw;
        }
    }
}
