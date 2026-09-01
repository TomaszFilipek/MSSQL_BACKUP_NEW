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

string MaskRecipient(string r)
{
    if (string.IsNullOrWhiteSpace(r)) return "(empty)";
    if (r.Length <= 12) return r;
    return r[..8] + "***" + r[^4..];
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

    var localCopySettings = new LocalCopySettings();
    configuration.GetSection("LocalCopySettings").Bind(localCopySettings);

    var ageSettings = new AgeSettings();
    configuration.GetSection("AgeSettings").Bind(ageSettings);

    var vpsSettings = new VpsSettings();
    configuration.GetSection("VpsSettings").Bind(vpsSettings);

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

    // Typ backupu: --type Full|Differential (wspolny dla wszystkich baz/serwerow), domyslnie z configu
    var typeArg = GetArg("type");
    BackupType effectiveType = backupSettings.DefaultType;
    if (!string.IsNullOrWhiteSpace(typeArg))
    {
        if (typeArg.Equals("diff", StringComparison.OrdinalIgnoreCase) ||
            typeArg.Equals("differential", StringComparison.OrdinalIgnoreCase))
            effectiveType = BackupType.Differential;
        else if (typeArg.Equals("full", StringComparison.OrdinalIgnoreCase))
            effectiveType = BackupType.Full;
        else
        {
            Console.Error.WriteLine($"Nieprawidlowa wartosc --type '{typeArg}'. Dozwolone: Full, Differential (Diff).");
            Environment.Exit(1);
        }
    }

    if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
    {
        Console.WriteLine("MssqlBackup.Console - uzycie:");
        Console.WriteLine("  MssqlBackup.Console [--server <Name>] [--type <Full|Differential>]");
        Console.WriteLine("  MssqlBackup.Console --sync-databases [server]");
        Console.WriteLine("  MssqlBackup.Console --test-vps [plik]");
        Console.WriteLine("  Opcje:");
        Console.WriteLine("    --server <Name>           Wykonaj tylko dla wskazanego serwera (Name lub Server)");
        Console.WriteLine("    --type <Full|Diff>        Typ backupu dla calej operacji (domyslnie z BackupSettings:DefaultType)");
        Console.WriteLine("    --sync-databases [server] Wysyla liste baz do API (drugi param = nazwa serwera, jesli brak to wszystkie)");
        Console.WriteLine("    --catalog, --sync-catalog Alias dla --sync-databases");
        Console.WriteLine("    --test-vps [plik]         Test polaczenia z VPS - wysyla pojedynczy plik via SCP (domyslnie tworzy plik tmp)");
        Console.WriteLine("  Uwagi: kolejność zawsze: BACKUP -> 7zip (bez hasła) -> LocalCopy (USB, przed age) -> age -r -> Samba/VPS (zaszyfrowany, po age) -> usuniecie .bak/.7z/.age");
        Console.WriteLine("         Folder z datą zawiera suffix typu: yyyy-MM-dd HH-mm-ss_Full | yyyy-MM-dd HH-mm-ss_Diff");
        Console.WriteLine("  Vps test: scp -i PrivateKeyPath plik user@host:RemotePath/_vps_test/");
        Console.WriteLine("  Dostepne serwery:");
        foreach (var s in servers!)
            Console.WriteLine($"    - {s.Name}: {s.Server} {(s.UseWindowsAuth ? "(WindowsAuth)" : $"User={s.Username}")}");
        return;
    }

    var isSync = args.Any(a => a.Equals("--sync-databases", StringComparison.OrdinalIgnoreCase) ||
                               a.Equals("--sync-catalog", StringComparison.OrdinalIgnoreCase) ||
                               a.Equals("--catalog", StringComparison.OrdinalIgnoreCase) ||
                               a.Equals("--list-databases", StringComparison.OrdinalIgnoreCase));

    var isTestVps = args.Any(a => a.Equals("--test-vps", StringComparison.OrdinalIgnoreCase) ||
                                  a.Equals("--test-vps-connection", StringComparison.OrdinalIgnoreCase) ||
                                  a.Equals("--vps-test", StringComparison.OrdinalIgnoreCase));

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
    services.AddTransient<LocalCopyService>();
    services.AddTransient<AgeService>();
    services.AddTransient<VpsService>();
    services.AddTransient<BackupOrchestrator>();

    var serviceProvider = services.BuildServiceProvider();

    if (isTestVps)
    {
        var testFileArg = GetArg("test-vps") ?? GetArg("test-vps-connection") ?? GetArg("vps-test");
        // GetArg returns empty string if flag without value, treat as null
        if (string.IsNullOrWhiteSpace(testFileArg)) testFileArg = null;
        // filter out if testFileArg looks like another flag
        if (testFileArg != null && testFileArg.StartsWith("-")) testFileArg = null;
        // also handle positional arg: --test-vps <file> without = (GetArg covers, but check direct)
        if (testFileArg == null)
        {
            var idx = Array.FindIndex(args, a => a.Equals("--test-vps", StringComparison.OrdinalIgnoreCase) ||
                                                a.Equals("--test-vps-connection", StringComparison.OrdinalIgnoreCase) ||
                                                a.Equals("--vps-test", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < args.Length && !args[idx + 1].StartsWith("-") && File.Exists(args[idx + 1]))
                testFileArg = args[idx + 1];
        }

        Console.WriteLine("=== TEST VPS CONNECTION (SCP) ===");
        Console.WriteLine($"VPS: {vpsSettings.Username}@{vpsSettings.Host}:{vpsSettings.RemotePath} (port {vpsSettings.Port})");
        Console.WriteLine($"Key: {(string.IsNullOrWhiteSpace(vpsSettings.PrivateKeyPath) ? "(password)" : vpsSettings.PrivateKeyPath)}");
        Console.WriteLine($"Enabled flag: {vpsSettings.Enabled} (test works even if Disabled, requires Host/Username/RemotePath)");
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(vpsSettings.Host) || string.IsNullOrWhiteSpace(vpsSettings.Username) || string.IsNullOrWhiteSpace(vpsSettings.RemotePath))
        {
            Console.Error.WriteLine("Blad: VpsSettings:Host, Username i RemotePath musza byc skonfigurowane w appsettings.json (VpsSettings)");
            Console.Error.WriteLine($"  Host='{vpsSettings.Host}', Username='{vpsSettings.Username}', RemotePath='{vpsSettings.RemotePath}'");
            Environment.Exit(1);
        }
        if (!string.IsNullOrWhiteSpace(vpsSettings.PrivateKeyPath) && !File.Exists(vpsSettings.PrivateKeyPath))
        {
            Console.Error.WriteLine($"Blad: plik klucza nie istnieje: {vpsSettings.PrivateKeyPath}");
            Environment.Exit(1);
        }

        string sourceFile;
        bool isTempFile = false;
        if (!string.IsNullOrWhiteSpace(testFileArg) && File.Exists(testFileArg))
        {
            sourceFile = testFileArg;
            Console.WriteLine($"Uzywam wskazanego pliku: {sourceFile} ({new FileInfo(sourceFile).Length} bytes)");
        }
        else
        {
            var tmpName = $"mssql_backup_vps_test_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.txt";
            sourceFile = Path.Combine(Path.GetTempPath(), tmpName);
            var content = $"MSSQL Backup - VPS connection test{Environment.NewLine}Date: {DateTime.UtcNow:O} (UTC){Environment.NewLine}Machine: {Environment.MachineName}{Environment.NewLine}VPS: {vpsSettings.Username}@{vpsSettings.Host}:{vpsSettings.RemotePath}{Environment.NewLine}File: {tmpName}{Environment.NewLine}Random: {Guid.NewGuid()}{Environment.NewLine}";
            await File.WriteAllTextAsync(sourceFile, content);
            isTempFile = true;
            Console.WriteLine($"Utworzono plik testowy: {sourceFile} ({new FileInfo(sourceFile).Length} bytes)");
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var remoteFileName = Path.GetFileName(sourceFile);
        // upload to RemotePath/_vps_test/ to avoid polluting
        var remoteFilePath = $"{vpsSettings.RemotePath.TrimEnd('/')}/_vps_test/{timestamp}_{remoteFileName}";
        Console.WriteLine($"Wysylam via SCP na: {vpsSettings.Username}@{vpsSettings.Host}:{remoteFilePath}");
        Console.WriteLine();

        var vpsService = serviceProvider.GetRequiredService<VpsService>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await vpsService.CopyToVpsAsync(sourceFile, remoteFilePath, vpsSettings);
            sw.Stop();
            Console.WriteLine();
            Console.WriteLine($"OK - plik wyslany w {sw.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"Zrodlo : {sourceFile}");
            Console.WriteLine($"Cel    : {vpsSettings.Username}@{vpsSettings.Host}:{remoteFilePath}");
            Console.WriteLine($"Sprawdz na VPS: ssh {vpsSettings.Username}@{vpsSettings.Host} \"ls -lh '{remoteFilePath}' && cat '{remoteFilePath}'\"");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"BLAD - wysylka nie powiodla sie po {sw.Elapsed.TotalSeconds:F1}s: {ex.Message}");
            Log.Error(ex, "Test VPS failed");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Wskazowki:");
            Console.Error.WriteLine("  - Sprawdz Host/Port/Username/RemotePath w appsettings.json (VpsSettings)");
            Console.Error.WriteLine("  - Sprawdz czy klucz istnieje i jest w formacie OpenSSH (nie .ppk)");
            Console.Error.WriteLine($"  - Na VPS: sudo mkdir -p {vpsSettings.RemotePath}/_vps_test && sudo chown {vpsSettings.Username}:{vpsSettings.Username} {vpsSettings.RemotePath}/_vps_test");
            Console.Error.WriteLine("  - Na Windows: skopiuj klucz publiczny: ssh-copy-id lub recznie authorized_keys");
            Console.Error.WriteLine("  - Sprawdz firewall/SSH: ssh -i <key> -p port user@host \"echo OK\"");
            Console.Error.WriteLine("  - Log: " + Path.Combine(logDir, "backup-.log"));
            Environment.Exit(1);
        }
        finally
        {
            if (isTempFile && File.Exists(sourceFile))
            {
                try { File.Delete(sourceFile); } catch { }
            }
        }
        return;
    }

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
        DefaultType = effectiveType,
        Compress = backupSettings.Compress,
        Verify = backupSettings.Verify,
        SendToApi = backupSettings.SendToApi,
        ExcludeDatabases = backupSettings.ExcludeDatabases,
        PostBackupCompression = compressionSettings,
        Samba = sambaSettings,
        LocalCopy = localCopySettings,
        Age = ageSettings,
        Vps = vpsSettings
    };

    Console.WriteLine("MssqlBackup.Console - Backup Orchestrator");
    Console.WriteLine($"Environment: {apiSettings.EnvironmentName}");
    Console.WriteLine($"API: {apiSettings.BaseUrl}");
    Console.WriteLine($"Backup type: {config.DefaultType} {(typeArg != null ? "(z --type)" : "(z configu)")}");
    Console.WriteLine($"Output: {config.OutputDirectory} / {{ENV}}/{{SERVER}}/{{yyyy-MM-dd HH-mm-ss}}_{(config.DefaultType == BackupType.Differential ? "Diff" : "Full")}");
    Console.WriteLine($"Post-backup compression: {config.PostBackupCompression.Compress} (delete source: {config.PostBackupCompression.DeleteSourceAfterCompress}) (7zip bez hasla - age uzywa -r)");
    Console.WriteLine($"Local copy (USB, przed age): {(config.LocalCopy.Enabled ? config.LocalCopy.DestinationPath : "disabled")} (kopiuje .7z przed szyfrowaniem, zachowuje niezaszyfrowany na USB)");
    Console.WriteLine($"Age encryption: {(config.Age.Enabled ? $"enabled (recipient: {MaskRecipient(config.Age.Recipient)})" : "disabled")} (age -r, szyfruje .7z -> .age, kopiuje zaszyfrowany na VPS/Samba)");
    Console.WriteLine($"Samba (po age, zaszyfrowany): {(config.Samba.Enabled ? config.Samba.SharePath : "disabled")} (kopiuje .age jesli age wlaczony, inaczej .7z)");
    Console.WriteLine($"VPS (po age, zaszyfrowany): {(config.Vps.Enabled ? $"{config.Vps.Username}@{config.Vps.Host}:{config.Vps.RemotePath} (port {config.Vps.Port}, key: {(string.IsNullOrWhiteSpace(config.Vps.PrivateKeyPath) ? "password" : config.Vps.PrivateKeyPath)})" : "disabled")} (SCP via scp/pscp, kopiuje .age)");
    var cleanupEnabled = (config.Vps.Enabled && config.Vps.DeleteSourceAfterCopy) || (config.Samba.Enabled && config.Samba.DeleteSourceAfterCopy);
    Console.WriteLine($"Cleanup po VPS/Samba: {(cleanupEnabled ? "usuwa .bak/.7z/.age z OutputDirectory po udanym upload" : "zachowuje intermediates (DeleteSourceAfterCopy=false lub brak remote)")}");
    Console.WriteLine($"Flow: BACKUP -> 7zip -> LocalCopy(USB, o ile Enabled) -> age -r (o ile Enabled) -> VPS/Samba(encrypted, o ile Enabled) -> delete intermediates");
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

        try
        {
            var result = await orchestrator.BackupAllDatabasesAsync(server, config, envName, srv.Name, servers.Count, srvIdx + 1);

            totalResult.TotalDatabases += result.TotalDatabases;
            totalResult.SuccessfulBackups += result.SuccessfulBackups;
            totalResult.FailedBackups += result.FailedBackups;
            totalResult.Errors.AddRange(result.Errors);

            Console.WriteLine($"Server {srv.Name}: {result.SuccessfulBackups}/{result.TotalDatabases} OK, {result.FailedBackups} errors");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error for server {Server}", srv.Name);
            Console.WriteLine($"Server {srv.Name}: nieoczekiwany błąd - {ex.Message} (job oznaczony jako Failed)");
            totalResult.FailedBackups++;
            totalResult.Errors.Add(new BackupError { DatabaseName = $"{srv.Name}/*", ErrorMessage = ex.Message });
        }
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
