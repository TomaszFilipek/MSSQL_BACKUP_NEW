# AGENTS.md

## Repository instruction
Po każdej zmianie wykonaj commit wszystkich zmian, z odpowiednim opisem

## Project change
Po każdej zmianie aktualizuj AGENTS.md

## Project Overview

MSSQL_BACKUP_NEW - projekt do zarządzania kopiami zapasowymi baz danych MSSQL.

## Project Structure

```
MSSQL_BACKUP_NEW/
├── MSSQL_BACKUP_NEW.slnx
├── .gitignore
├── README.md
├── Dockerfile                 # Multi-stage build dla API
├── Dockerfile.web             # Multi-stage build dla Web
├── docker-compose.yml         # API + Web + wolumen dla SQLite
├── .dockerignore
├── deploy-to-nas.ps1          # Skrypt deploy na NAS (PuTTY)
└── src/
    ├── MssqlBackup.Shared/           # Współdzielona biblioteka (.NET 9 class library)
    │   ├── MssqlBackup.Shared.csproj
    │   ├── Models/                   # Wspólne modele (DTO, encje)
    │   └── Interfaces/               # Wspólne interfejsy
    ├── MssqlBackup.Api/              # REST API (.NET 9 web API)
    │   ├── MssqlBackup.Api.csproj
    │   ├── Program.cs
    │   ├── Controllers/
    │   │   ├── BackupRecordsController.cs  # CRUD + SignalR broadcast
    │   │   └── BackupJobsController.cs     # CRUD dla live status konsoli
    │   ├── Hubs/
    │   │   └── BackupHub.cs          # SignalR hub (BackupCreated + JobUpdated)
    │   ├── Data/
    │   │   ├── AppDbContext.cs       # EF Core DbContext (SQLite)
    │   │   └── Migrations/           # Migracje EF Core
    │   ├── Models/
    │   │   ├── BackupRecord.cs       # Encja historii backupów
    │   │   └── BackupJob.cs          # Encja live statusu konsoli
    │   ├── appsettings.json
    │   └── appsettings.Development.json
    ├── MssqlBackup.Web/             # Blazor Server UI (.NET 9)
    │   ├── MssqlBackup.Web.csproj
    │   ├── Program.cs
    │   ├── Services/
    │   │   ├── BackupApiService.cs   # Klient HTTP do API (backuprecords)
    │   │   └── BackupJobService.cs   # Klient HTTP do API (backupjobs)
    │   ├── Models/
    │   │   ├── BackupRecordDto.cs    # DTO + BackupFilter
    │   │   └── BackupJobDto.cs       # DTO dla live statusu
    │   ├── Helpers/
    │   │   └── TimeHelper.cs         # Konwersja UTC -> Europe/Warsaw
    │   ├── Hubs/
    │   │   └── BackupHub.cs          # SignalR hub client
    │   ├── Components/
    │   │   ├── App.razor
    │   │   ├── _Imports.razor
    │   │   ├── Layout/
    │   │   │   ├── MainLayout.razor
    │   │   │   └── NavMenu.razor
    │   │   └── Pages/
    │   │       ├── Home.razor        # Dashboard
    │   │       ├── Backups.razor     # Historia z filtrami
    │   │       ├── LatestBackups.razor # Ostatnie backupy
    │   │       ├── Jobs.razor        # Live status konsoli (aktywne + historia)
    │   │       └── Test.razor        # Strona diagnostyczna
    │   ├── wwwroot/css/app.css
    │   └── appsettings.json
    └── MssqlBackup.Console/          # Aplikacja konsolowa (.NET 9 console)
        ├── MssqlBackup.Console.csproj
        ├── Program.cs                # Serilog file logging 14d rotation
        ├── appsettings.json           # Konfiguracja API, serwera, backupu, kompresji, Samba
        ├── appsettings.Local.json     # Local overrides (gitignored)
        ├── Models/
        │   ├── BackupType.cs
        │   ├── BackupOptions.cs
        │   ├── BackupConfiguration.cs
        │   ├── BackupSettings.cs
        │   ├── ServerConnection.cs
        │   ├── ServerSettings.cs
        │   ├── BackupResult.cs
        │   ├── BackupError.cs
        │   ├── ApiSettings.cs
        │   ├── BackupRecordDto.cs
        │   ├── BackupJobDto.cs       # DTO live statusu
        │   ├── CompressionSettings.cs # + DeleteSourceAfterCompress
        │   └── SambaSettings.cs
        └── Services/
            ├── BackupService.cs
            ├── BackupOrchestrator.cs  # Raportuje progress do API (BackupJob)
            ├── BackupApiClient.cs
            ├── BackupJobApiClient.cs  # Klient HTTP dla BackupJobs
            ├── CompressionService.cs
            └── SambaService.cs
```

## Key Technical Decisions

- **Framework**: .NET 9 (STS)
- **ORM**: Entity Framework Core 9.x (dla REST API)
- **Baza danych**: SQLite (EntityFrameworkCore.Sqlite) - wybrane ze względu na ograniczenia RAM na NAS
- **Namespace**: MssqlBackup.*
- **API**: Kontrollery (nie Minimal API)
- **Konfiguracja**: appsettings.json + appsettings.Development.json
- **Backup SQL**: Microsoft.Data.SqlClient + BACKUP DATABASE

## Dependencies

| Project | References | NuGet Packages |
|---------|-----------|----------------|
| MssqlBackup.Shared | — | — |
| MssqlBackup.Console | MssqlBackup.Shared | Microsoft.Extensions.Configuration, Microsoft.Extensions.Configuration.Json, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging.Console, Microsoft.Extensions.Http, Microsoft.Data.SqlClient, Serilog.Extensions.Hosting, Serilog.Sinks.Console, Serilog.Sinks.File |
| MssqlBackup.Api | MssqlBackup.Shared | Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Sqlite, Microsoft.EntityFrameworkCore.Design, Scalar.AspNetCore |
| MssqlBackup.Web | MssqlBackup.Shared | Microsoft.AspNetCore.SignalR.Client (9.0.10) |

## Console App Architecture

### Kluczowe klasy
- **BackupService** - wykonuje backupy pojedynczych baz (BACKUP DATABASE)
- **BackupOrchestrator** - orchestruje backup wszystkich baz + raportuje live status do API (BackupJob)
- **BackupApiClient** - klient HTTP do wysyłania rekordów do REST API
- **BackupJobApiClient** - klient HTTP do raportowania live statusu (BackupJob: Running/Completed)
- **CompressionService** - kompresja plików 7-Zip (z obsługą hasła + DeleteSourceAfterCompress)
- **SambaService** - wysyłka backupów na udziały sieciowe Samba
- **ServerConnection** - dane połączenia (Server, Username, Password, UseWindowsAuth)
- **BackupConfiguration** - konfiguracja orchestratora (OutputDirectory, ExcludeDatabases, Compress, Verify)
- **BackupOptions** - parametry backupu (DatabaseName, OutputPath, Type, Compress, Verify)
- **BackupResult** - wynik operacji (TotalDatabases, SuccessfulBackups, FailedBackups, Errors)
- **ApiSettings** - ustawienia API (BaseUrl, EnvironmentName)
- **CompressionSettings** - ustawienia kompresji (Compress, Password, CompressionLevel, DeleteSourceAfterCompress)
- **SambaSettings** - ustawienia Samba (Enabled, SharePath, DeleteSourceAfterCopy, CreateOkFile)

### Przykład użycia
```csharp
var orchestrator = serviceProvider.GetRequiredService<BackupOrchestrator>();

var server = new ServerConnection { Server = @".\SQLEXPRESS", UseWindowsAuth = true };
var config = new BackupConfiguration
{
    OutputDirectory = @"C:\Backups\MSSQL",
    DefaultType = BackupType.Full,
    Compress = true,
    Verify = true,
    ExcludeDatabases = ["master", "model", "msdb", "tempdb"],
    PostBackupCompression = new CompressionSettings
    {
        Compress = true,
        Password = "MySecretPassword123",
        CompressionLevel = "Normal",
        DeleteSourceAfterCompress = true
    },
    Samba = new SambaSettings
    {
        Enabled = true,
        SharePath = @"\\192.168.1.2\backups",
        DeleteSourceAfterCopy = true,
        CreateOkFile = true
    }
};

var result = await orchestrator.BackupAllDatabasesAsync(server, config, environmentName: "Production");
```

## API Architecture

### BackupRecordsController

| Method | Endpoint | Opis |
|--------|----------|------|
| GET | `/api/backuprecords` | Lista (filtry: environment, instance, database, from, to) |
| GET | `/api/backuprecords/latest` | Ostatni backup każdej bazy |
| GET | `/api/backuprecords/{id}` | Pojedynczy rekord |
| POST | `/api/backuprecords` | Utwórz (+ broadcast `BackupCreated`) |
| PUT | `/api/backuprecords/{id}` | Aktualizuj |
| DELETE | `/api/backuprecords/{id}` | Usuń |

### BackupJobsController (live status konsoli)

| Method | Endpoint | Opis |
|--------|----------|------|
| GET | `/api/backupjobs` | Lista (filtry: environment, instance, status, take) |
| GET | `/api/backupjobs/active` | Tylko `Running` |
| GET | `/api/backupjobs/{id}` | Pojedynczy job |
| POST | `/api/backupjobs` | Utwórz (broadcast `JobCreated` + `JobUpdated`) |
| PUT | `/api/backupjobs/{id}` | Aktualizuj (broadcast `JobUpdated`/`JobFinished`) |
| DELETE | `/api/backupjobs/{id}` | Usuń |

### BackupRecord Model
- EnvironmentName, InstanceName, DatabaseName, BackupType
- OutputFilePath, FileSize, BackupDate
- Compress, Verify, Duration

### BackupJob Model (live)
- EnvironmentName, InstanceName, HostName, Status (Running/Completed/Failed)
- StartedAt, FinishedAt, UpdatedAt (UTC)
- TotalDatabases, CompletedCount, FailedCount
- CurrentDatabase, CurrentStep, Message

### Konfiguracja
- **CORS**: AllowAnyOrigin (dla instancji konsolowych na VPS)
- **AutoMigrate**: Automatyczna migracja przy starcie
- **API Documentation**: Scalar (zamiast Swagger UI) - dostępne pod `/scalar/v1` w trybie Development
- **SignalR**: `/hubs/backup` - `BackupCreated`, `JobCreated`, `JobUpdated`, `JobFinished`

## Development

### Build
```bash
dotnet build
```

### Run API
```bash
dotnet run --project src/MssqlBackup.Api
```

### Run Console
```bash
# Wszystkie skonfigurowane serwery
dotnet run --project src/MssqlBackup.Console

# Konkretny serwer (Name lub Server)
dotnet run --project src/MssqlBackup.Console -- --server PROD-01
dotnet run --project src/MssqlBackup.Console -- --server ".\SQLEXPRESS"
```

### EF Core Migrations
```bash
# Add migration
dotnet ef migrations add <MigrationName> --project src/MssqlBackup.Api

# Update database
dotnet ef database update --project src/MssqlBackup.Api
```

## Przykłady konfiguracji

### API - `appsettings.json`
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=MssqlBackup.db"
  }
}
```

### API - `appsettings.Development.json`
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=MssqlBackup_Dev.db"
  }
}
```

### Console - `appsettings.json`
```json
{
  "ApiSettings": {
    "BaseUrl": "http://localhost:5142",
    "EnvironmentName": "Production"
  },
  "Servers": [
    {
      "Name": "PROD-01",
      "Server": ".\\SQLEXPRESS",
      "Database": null,
      "Username": null,
      "Password": null,
      "UseWindowsAuth": true
    }
  ],
  "BackupSettings": {
    "OutputDirectory": "C:\\Backups\\MSSQL",
    "DefaultType": "Full",
    "Compress": true,
    "Verify": true,
    "ExcludeDatabases": ["master", "model", "msdb", "tempdb"]
  },
  "CompressionSettings": {
    "Compress": true,
    "Password": "",
    "CompressionLevel": "Normal",
    "DeleteSourceAfterCompress": false
  },
  "SambaSettings": {
    "Enabled": false,
    "SharePath": "\\\\192.168.1.2\\backups",
    "Username": null,
    "Password": null,
    "Domain": null,
    "DeleteSourceAfterCopy": false,
    "CreateOkFile": false
  }
}
```

### Console - `Program.cs` (odczyt konfiguracji z appsettings + wybór serwera)
```csharp
var apiSettings = new ApiSettings();
configuration.GetSection("ApiSettings").Bind(apiSettings);

// Servers: prefer "Servers" array, fallback to legacy "ServerSettings"
var servers = configuration.GetSection("Servers").Get<List<NamedServerSettings>>();
if (servers == null || servers.Count == 0)
{
    var legacy = new ServerSettings();
    configuration.GetSection("ServerSettings").Bind(legacy);
    servers = [new NamedServerSettings { Name = "Default", Server = legacy.Server }];
}
// Filtrowanie: --server <Name>
var requested = args.FirstOrDefault(a => a.StartsWith("--server"));
if (requested != null) servers = servers.Where(s => s.Name == requested).ToList();

var backupSettings = new BackupSettings();
configuration.GetSection("BackupSettings").Bind(backupSettings);

var compressionSettings = new CompressionSettings();
configuration.GetSection("CompressionSettings").Bind(compressionSettings);

var sambaSettings = new SambaSettings();
configuration.GetSection("SambaSettings").Bind(sambaSettings);
```

## Docker

### Struktura plików Docker
```
MSSQL_BACKUP_NEW/
├── Dockerfile              # Multi-stage build dla API
├── Dockerfile.web          # Multi-stage build dla Web
├── docker-compose.yml      # API + Web + wolumen dla SQLite
├── .dockerignore           # Wykluczenia z kontekstu buildu
└── deploy-to-nas.ps1       # Skrypt deploy na NAS (PuTTY)
```

### Uruchomienie na VPS
```bash
# 1. Sklonuj repozytorium
git clone <repo-url>
cd MSSQL_BACKUP_NEW

# 2. Zbuduj i uruchom
docker compose up -d --build

# 3. Sprawdź status
docker compose ps
docker compose logs api
```

### Endpoints po uruchomieniu
- API: `http://<VPS_IP>:8283`
- Web UI: `http://<VPS_IP>:8284`
- Scalar API Docs: `http://<VPS_IP>:8283/scalar/v1` (w trybie Development)

### Zarządzanie
```bash
# Zatrzymanie
docker compose down

# Usunięcie z danymi
docker compose down -v

# Logi
docker compose logs -f api
```

## Notes

- Aplikacja konsolowa korzysta z innej bazy danych niż REST API
- Migracje EF Core znajdują się w projekcie API (Data/Migrations/)
- Połączenie z bazą danych jest konfigurowane przez appsettings.json
- BackupOrchestrator pomija domyślnie bazy systemowe (master, model, msdb, tempdb)
- Pliki backupów są zapisywane w `[OutputDirectory]/[EnvironmentName]/[yyyy-MM-dd HH-mm-ss]/` (ta sama struktura lokalnie i na Sambie)
- Błędy podczas backupu pojedynczych baz są logowane, a operacja jest kontynuowana
- W Dockerze API nasłuchuje na porcie 5000 (HTTP)
- SQL Server w Dockerze używa domyślnie hasła z pliku .env
- Deploy na NAS: `./deploy-to-nas.ps1` (wymaga PuTTY - plink/pscp)
