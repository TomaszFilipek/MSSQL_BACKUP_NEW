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

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddTransient<BackupService>();

var serviceProvider = services.BuildServiceProvider();

var backupService = serviceProvider.GetRequiredService<BackupService>();

var server = new ServerConnection
{
    Server = @".\SQLEXPRESS",
    UseWindowsAuth = true
};

Console.WriteLine("MssqlBackup.Console started");
Console.WriteLine("Available databases:");

var databases = await backupService.GetDatabasesAsync(server);
foreach (var db in databases)
{
    Console.WriteLine($"  - {db}");
}
