using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;
using MssqlBackup.Console.Services;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .Build();

var apiSettings = new ApiSettings();
configuration.GetSection("ApiSettings").Bind(apiSettings);

var serverSettings = new ServerSettings();
configuration.GetSection("ServerSettings").Bind(serverSettings);

var backupSettings = new BackupSettings();
configuration.GetSection("BackupSettings").Bind(backupSettings);

var compressionSettings = new CompressionSettings();
configuration.GetSection("CompressionSettings").Bind(compressionSettings);

var sambaSettings = new SambaSettings();
configuration.GetSection("SambaSettings").Bind(sambaSettings);

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddHttpClient<BackupApiClient>(client =>
{
    client.BaseAddress = new Uri(apiSettings.BaseUrl);
});
services.AddTransient<BackupService>();
services.AddTransient<CompressionService>();
services.AddTransient<SambaService>();
services.AddTransient<BackupOrchestrator>();

var serviceProvider = services.BuildServiceProvider();

var orchestrator = serviceProvider.GetRequiredService<BackupOrchestrator>();

var server = new ServerConnection
{
    Server = serverSettings.Server,
    Database = serverSettings.Database,
    Username = serverSettings.Username,
    Password = serverSettings.Password,
    UseWindowsAuth = serverSettings.UseWindowsAuth
};

var config = new BackupConfiguration
{
    OutputDirectory = backupSettings.OutputDirectory,
    DefaultType = backupSettings.DefaultType,
    Compress = backupSettings.Compress,
    Verify = backupSettings.Verify,
    SendToApi = backupSettings.SendToApi,
    ExcludeDatabases = backupSettings.ExcludeDatabases,
    PostBackupCompression = compressionSettings,
    Samba = sambaSettings
};

Console.WriteLine("MssqlBackup.Console - Backup Orchestrator");
Console.WriteLine($"Server: {server.Server}");
Console.WriteLine($"Output: {config.OutputDirectory}");
Console.WriteLine($"Environment: {apiSettings.EnvironmentName}");
Console.WriteLine($"API: {apiSettings.BaseUrl}");
Console.WriteLine($"Post-backup compression: {config.PostBackupCompression.Compress}");
Console.WriteLine($"Samba share: {(config.Samba.Enabled ? config.Samba.SharePath : "disabled")}");
Console.WriteLine();

var result = await orchestrator.BackupAllDatabasesAsync(server, config, apiSettings.EnvironmentName);

Console.WriteLine();
Console.WriteLine("=== Backup Summary ===");
Console.WriteLine($"Total databases: {result.TotalDatabases}");
Console.WriteLine($"Successful: {result.SuccessfulBackups}");
Console.WriteLine($"Failed: {result.FailedBackups}");

if (result.Errors.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Errors:");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"  - {error.DatabaseName}: {error.ErrorMessage}");
    }
}
