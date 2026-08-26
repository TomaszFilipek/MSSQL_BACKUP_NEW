using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;
using MssqlBackup.Console.Services;
using Serilog;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .Build();

var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(logDir, "backup-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

string? GetArg(string name)
{
    for (int i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (a.Equals($"--{name}", StringComparison.OrdinalIgnoreCase) ||
            a.Equals($"-{name[0]}", StringComparison.OrdinalIgnoreCase) ||
            a.Equals($"/{name}", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < args.Length) return args[i + 1];
            return string.Empty;
        }
        if (a.StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith($"--{name}:", StringComparison.OrdinalIgnoreCase))
            return a.Substring(a.IndexOfAny(['=', ':']) + 1);
        if (a.StartsWith($"-{name[0]}=", StringComparison.OrdinalIgnoreCase))
            return a.Substring(2);
    }
    return null;
}

try
{
    var apiSettings = new ApiSettings();
    configuration.GetSection("ApiSettings").Bind(apiSettings);

    var backupSettings = new BackupSettings();
    configuration.GetSection("BackupSettings").Bind(backupSettings);

    var compressionSettings = new CompressionSettings();
    configuration.GetSection("CompressionSettings").Bind(compressionSettings);

    var sambaSettings = new SambaSettings();
    configuration.GetSection("SambaSettings").Bind(sambaSettings);

    // Load servers: prefer "Servers" array, fallback to legacy "ServerSettings"
    var servers = configuration.GetSection("Servers").Get<List<NamedServerSettings>>();
    if (servers == null || servers.Count == 0)
    {
        var legacy = new ServerSettings();
        configuration.GetSection("ServerSettings").Bind(legacy);
        // treat as configured if Server is not empty
        if (!string.IsNullOrWhiteSpace(legacy.Server))
        {
            servers = [new NamedServerSettings
            {
                Name = "Default",
                Server = legacy.Server!,
                Database = legacy.Database,
                Username = legacy.Username,
                Password = legacy.Password,
                UseWindowsAuth = legacy.UseWindowsAuth
            }];
        }
    }

    if (servers == null || servers.Count == 0)
    {
        Console.Error.WriteLine("Brak zdefiniowanych serwerow w konfiguracji (Servers lub ServerSettings).");
        Environment.Exit(1);
    }

    var requestedServer = GetArg("server");
    if (!string.IsNullOrWhiteSpace(requestedServer))
    {
        var filtered = servers!.Where(s => s.Name.Equals(requestedServer, StringComparison.OrdinalIgnoreCase) ||
                                          s.Server.Equals(requestedServer, StringComparison.OrdinalIgnoreCase)).ToList();
        if (filtered.Count == 0)
        {
            Console.Error.WriteLine($"Nie znaleziono serwera '{requestedServer}'. Dostepne: {string.Join(", ", servers!.Select(s => s.Name))}");
            Console.Error.WriteLine("Uzyj: --server <Name>  lub  --server=<Name>");
            Environment.Exit(1);
        }
        servers = filtered;
    }

    if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
    {
        Console.WriteLine("MssqlBackup.Console - uzycie:");
        Console.WriteLine("  MssqlBackup.Console [--server <Name>] [--sync-databases [server]]");
        Console.WriteLine("  --sync-databases | --catalog : wysyla liste baz do API (drugi param = nazwa serwera, jesli brak to wszystkie)");
        Console.WriteLine("  Dostepne serwery:");
        foreach (var s in servers!)
            Console.WriteLine($"    - {s.Name}: {s.Server} {(s.UseWindowsAuth ? "(WindowsAuth)" : $"User={s.Username}")}");
        return;
    }

    var isSync = args.Any(a => a.Equals("--sync-databases", StringComparison.OrdinalIgnoreCase) ||
                               a.Equals("--sync-catalog", StringComparison.OrdinalIgnoreCase) ||
                               a.Equals("--catalog", StringComparison.OrdinalIgnoreCase) ||
                               a.Equals("--list-databases", StringComparison.OrdinalIgnoreCase));

    // Obsluga drugiego parametru pozycyjnego dla sync: --sync-databases PROD-01
    if (isSync && string.IsNullOrWhiteSpace(requestedServer))
    {
        var syncIdx = Array.FindIndex(args, a => a.Equals("--sync-databases", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Equals("--sync-catalog", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Equals("--catalog", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Equals("--list-databases", StringComparison.OrdinalIgnoreCase));
        if (syncIdx >= 0 && syncIdx + 1 < args.Length && !args[syncIdx + 1].StartsWith("-"))
        {
            var positional = args[syncIdx + 1];
            var match = servers!.FirstOrDefault(s => s.Name.Equals(positional, StringComparison.OrdinalIgnoreCase) ||
                                                     s.Server.Equals(positional, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                servers = [match];
                requestedServer = positional;
            }
        }
    }

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddLogging(builder =>
    {
        builder.ClearProviders();
        builder.AddSerilog(Log.Logger, dispose: true);
    });
    services.AddHttpClient<BackupApiClient>(client => { client.BaseAddress = new Uri(apiSettings.BaseUrl); });
    services.AddHttpClient<BackupJobApiClient>(client => { client.BaseAddress = new Uri(apiSettings.BaseUrl); });
    services.AddHttpClient<DatabaseCatalogApiClient>(client => { client.BaseAddress = new Uri(apiSettings.BaseUrl); });
    services.AddTransient<BackupService>();
    services.AddTransient<CompressionService>();
    services.AddTransient<SambaService>();
    services.AddTransient<BackupOrchestrator>();

    var serviceProvider = services.BuildServiceProvider();

    if (isSync)
    {
        var backupService = serviceProvider.GetRequiredService<BackupService>();
        var catalogClient = serviceProvider.GetRequiredService<DatabaseCatalogApiClient>();
        Console.WriteLine($"=== SYNC DATABASES TO API ({servers!.Count} serwerow) ===");
        Console.WriteLine($"Environment: {apiSettings.EnvironmentName}");
        Console.WriteLine();
        foreach (var srv in servers!)
        {
            var conn = srv.ToConnection();
            Console.WriteLine($"-- {srv.Name} ({conn.Server}) --");
            try
            {
                var dbs = await backupService.GetDatabasesAsync(conn);
                // apply exclude filter so catalog reflects what would be backed up
                var toSync = BackupOrchestrator.FilterDatabases(dbs, backupSettings.ExcludeDatabases);
                Console.WriteLine($"   Znaleziono {dbs.Count} baz, po filtrowaniu: {toSync.Count}");
                await catalogClient.SyncAsync(apiSettings.EnvironmentName, conn.Server, srv.Name, toSync);
                Console.WriteLine($"   Zsynchronizowano {toSync.Count} baz");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Blad: {ex.Message}");
                Log.Error(ex, "Sync failed for {Server}", srv.Name);
            }
        }
        Console.WriteLine("Sync zakonczony.");
        return;
    }

    var orchestrator = serviceProvider.GetRequiredService<BackupOrchestrator>();

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
    Console.WriteLine($"Environment: {apiSettings.EnvironmentName}");
    Console.WriteLine($"API: {apiSettings.BaseUrl}");
    Console.WriteLine($"Output: {config.OutputDirectory} / {{ENV}}/{{SERVER}}/{{yyyy-MM-dd HH-mm-ss}}");
    Console.WriteLine($"Post-backup compression: {config.PostBackupCompression.Compress} (delete source: {config.PostBackupCompression.DeleteSourceAfterCompress})");
    Console.WriteLine($"Samba share: {(config.Samba.Enabled ? config.Samba.SharePath : "disabled")} (same structure)");
    Console.WriteLine($"Log file: {Path.Combine(logDir, "backup-.log")} (retention 14 days)");
    Console.WriteLine($"Serwery do przetworzenia: {servers!.Count} ({string.Join(", ", servers.Select(s => s.Name))})");
    if (!string.IsNullOrWhiteSpace(requestedServer))
        Console.WriteLine($"Wybrany serwer (filtr): {requestedServer}");
    Console.WriteLine();

    var totalResult = new BackupResult { TotalDatabases = 0 };

    for (int srvIdx = 0; srvIdx < servers!.Count; srvIdx++)
    {
        var srv = servers[srvIdx];
        var server = srv.ToConnection();
        var envName = apiSettings.EnvironmentName; // global env for all servers
        Console.WriteLine($"=== Server: {srv.Name} ({server.Server}) [{srvIdx + 1}/{servers.Count}] ===");
        Log.Information("Processing server {ServerName} ({Server}) {Index}/{Total}", srv.Name, server.Server, srvIdx + 1, servers.Count);

        var result = await orchestrator.BackupAllDatabasesAsync(server, config, envName, srv.Name, servers.Count, srvIdx + 1);

        totalResult.TotalDatabases += result.TotalDatabases;
        totalResult.SuccessfulBackups += result.SuccessfulBackups;
        totalResult.FailedBackups += result.FailedBackups;
        totalResult.Errors.AddRange(result.Errors);

        Console.WriteLine($"Server {srv.Name}: {result.SuccessfulBackups}/{result.TotalDatabases} OK, {result.FailedBackups} errors");
        Console.WriteLine();
    }

    Console.WriteLine("=== Backup Summary (all servers) ===");
    Console.WriteLine($"Total databases: {totalResult.TotalDatabases}");
    Console.WriteLine($"Successful: {totalResult.SuccessfulBackups}");
    Console.WriteLine($"Failed: {totalResult.FailedBackups}");

    if (totalResult.Errors.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Errors:");
        foreach (var error in totalResult.Errors)
            Console.WriteLine($"  - {error.DatabaseName}: {error.ErrorMessage}");
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
