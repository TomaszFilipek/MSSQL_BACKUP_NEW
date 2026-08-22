using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;
using MssqlBackup.Console.Services;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .Build();

var apiSettings = new ApiSettings();
configuration.GetSection("ApiSettings").Bind(apiSettings);

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
services.AddTransient<BackupOrchestrator>();

var serviceProvider = services.BuildServiceProvider();

var orchestrator = serviceProvider.GetRequiredService<BackupOrchestrator>();

var server = new ServerConnection
{
    Server = @".\SQLEXPRESS",
    UseWindowsAuth = true
};

var config = new BackupConfiguration
{
    OutputDirectory = @"C:\Backups\MSSQL",
    DefaultType = BackupType.Full,
    Compress = true,
    Verify = true,
    ExcludeDatabases = ["master", "model", "msdb", "tempdb"]
};

Console.WriteLine("MssqlBackup.Console - Backup Orchestrator");
Console.WriteLine($"Server: {server.Server}");
Console.WriteLine($"Output: {config.OutputDirectory}");
Console.WriteLine($"Environment: {apiSettings.EnvironmentName}");
Console.WriteLine($"API: {apiSettings.BaseUrl}");
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
