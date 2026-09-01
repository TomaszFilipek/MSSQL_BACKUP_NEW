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
    │   │   ├── BackupJobsController.cs     # CRUD dla live status konsoli
    │   │   └── DatabasesController.cs      # sync + katalog baz (RegisteredDatabase)
    │   ├── Hubs/
    │   │   └── BackupHub.cs          # SignalR hub (BackupCreated + JobUpdated)
    │   ├── Data/
    │   │   ├── AppDbContext.cs       # EF Core DbContext (SQLite)
    │   │   └── Migrations/           # Migracje EF Core
    │   ├── Models/
    │   │   ├── BackupRecord.cs       # Encja historii backupów
    │   │   ├── BackupJob.cs          # Encja live statusu konsoli
    │   │   └── RegisteredDatabase.cs # Encja katalogu baz (DatabaseKey = ENV|Instance|DB)
    │   ├── appsettings.json
    │   └── appsettings.Development.json
    ├── MssqlBackup.Web/             # Blazor Server UI (.NET 9)
    │   ├── MssqlBackup.Web.csproj
    │   ├── Program.cs
    │   ├── Services/
    │   │   ├── BackupApiService.cs         # Klient HTTP do API (backuprecords)
    │   │   ├── BackupJobService.cs         # Klient HTTP do API (backupjobs)
    │   │   └── DatabaseCatalogService.cs   # Klient HTTP do API (databases)
    │   ├── Models/
    │   │   ├── BackupRecordDto.cs          # DTO + BackupFilter
    │   │   ├── BackupJobDto.cs             # DTO dla live statusu
    │   │   └── DatabaseCatalogDto.cs       # DTO katalogu baz (z ostatnim backupem)
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
    │   │       ├── Databases.razor   # Katalog baz (ostatni backup, sort, filtry, wiek)
    │   │       └── Test.razor        # Strona diagnostyczna
    │   ├── wwwroot/css/app.css
    │   └── appsettings.json
    └── MssqlBackup.Console/          # Aplikacja konsolowa (.NET 9 console)
        ├── MssqlBackup.Console.csproj
        ├── Program.cs                # Serilog file logging 14d rotation + --server/--type/--sync-databases
        ├── appsettings.json           # Konfiguracja API, serwera, backupu, kompresji, Samba, LocalCopy, Age, Vps
        ├── appsettings.Local.json     # Local overrides (gitignored)
        ├── Models/
        │   ├── BackupType.cs         # Full, Differential
        │   ├── BackupOptions.cs
        │   ├── BackupConfiguration.cs  # + LocalCopy + Age + Vps
        │   ├── BackupSettings.cs
        │   ├── ServerConnection.cs
        │   ├── ServerSettings.cs
        │   ├── NamedServerSettings.cs
        │   ├── BackupResult.cs
        │   ├── BackupError.cs
        │   ├── ApiSettings.cs
        │   ├── BackupRecordDto.cs
        │   ├── BackupJobDto.cs       # DTO live statusu
        │   ├── CompressionSettings.cs # + DeleteSourceAfterCompress
        │   ├── SambaSettings.cs
        │   ├── LocalCopySettings.cs
        │   ├── AgeSettings.cs        # Enabled, Recipient (-r), RecipientsFile, AgePath
        │   └── VpsSettings.cs        # Enabled, Host, Port, Username, PrivateKeyPath, RemotePath
        └── Services/
            ├── BackupService.cs
            ├── BackupOrchestrator.cs  # Raportuje progress do API (BackupJob), flow: backup -> 7zip -> USB -> age -> VPS/Samba -> cleanup
            ├── BackupApiClient.cs
            ├── BackupJobApiClient.cs  # Klient HTTP dla BackupJobs
            ├── DatabaseCatalogApiClient.cs
            ├── CompressionService.cs   # 7-Zip (bez hasla - age uzywa -r)
            ├── SambaService.cs
            ├── LocalCopyService.cs     # Kopia na USB/przed age (niezaszyfrowana, .7z)
            ├── AgeService.cs           # age -r recipient (--encrypt -r age1... -o .age)
            └── VpsService.cs           # SCP (scp/pscp) na VPS (zaszyfrowany .age, mkdir -p)
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
- **BackupOrchestrator** - orchestruje backup wszystkich baz + raportuje live status do API (BackupJob), kolejność: backup -> 7zip (bez hasla) -> USB (LocalCopy, przed age) -> age -r -> VPS/Samba (zaszyfrowany, po age) -> delete .bak/.7z/.age, folder `yyyy-MM-dd HH-mm-ss_Full/_Diff`, auto-finalizacja `Failed` przy nieoczekiwanym wyjątku (podwójna ochrona ze stale-check)
- **BackupApiClient** - klient HTTP do wysyłania rekordów do REST API
- **BackupJobApiClient** - klient HTTP do raportowania live statusu (BackupJob: Running/Completed)
- **DatabaseCatalogApiClient** - wysyłka listy baz do API (`POST /api/databases/sync`)
- **CompressionService** - kompresja plików 7-Zip (bez hasla - age uzywa -r, + DeleteSourceAfterCompress)
- **SambaService** - wysyłka backupów na udziały sieciowe Samba (po age, zaszyfrowany .age jesli Age.Enabled)
- **LocalCopyService** - kopia backupu/archiwum do folderu lokalnego (USB, przed age, niezaszyfrowana .7z, zawsze przed szyfrowaniem)
- **AgeService** - szyfrowanie age (--encrypt -r <recipient> -o .age), szuka age.exe w PATH + znanych sciezkach, waliduje rozmiar po szyfrowaniu
- **VpsService** - wysylka zaszyfrowanego .age na VPS via SCP (scp.exe OpenSSH lub pscp.exe PuTTY, mkdir -p przez ssh/plink, BatchMode=yes)
- **ServerConnection** - dane połączenia (Server, Username, Password, UseWindowsAuth)
- **BackupConfiguration** - konfiguracja orchestratora (OutputDirectory, ExcludeDatabases, Compress, Verify, Samba, LocalCopy, Age, Vps)
- **BackupOptions** - parametry backupu (DatabaseName, OutputPath, Type, Compress, Verify)
- **BackupResult** - wynik operacji (TotalDatabases, SuccessfulBackups, FailedBackups, Errors)
- **ApiSettings** - ustawienia API (BaseUrl, EnvironmentName)
- **CompressionSettings** - ustawienia kompresji (Compress, Password (puste gdy age), CompressionLevel, DeleteSourceAfterCompress)
- **SambaSettings** - ustawienia Samba (Enabled, SharePath, DeleteSourceAfterCopy, CreateOkFile) - kopiuje .age jesli Age.Enabled
- **LocalCopySettings** - kopiowanie lokalne (Enabled, DestinationPath, DeleteSourceAfterCopy) - zawsze PRZED age (USB niezaszyfrowany)
- **AgeSettings** - szyfrowanie age (Enabled, Recipient age1..., RecipientsFile, AgePath=age)
- **VpsSettings** - VPS SCP (Enabled, Host, Port=22, Username, PrivateKeyPath, RemotePath=/mnt/backups, DeleteSourceAfterCopy=true)

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
        Password = "", // puste - szyfrowanie robi age -r, nie 7zip
        CompressionLevel = "Normal",
        DeleteSourceAfterCompress = false // zostanie usuniety po age+VPS
    },
    LocalCopy = new LocalCopySettings
    {
        Enabled = true,
        DestinationPath = @"E:\USB\Backups", // USB: kopia .7z PRZED age (niezaszyfrowana)
        DeleteSourceAfterCopy = false
    },
    Age = new AgeSettings
    {
        Enabled = true,
        Recipient = "age1ql3z7hj432v2jl2z8alunwwun8ap59a43lfcw80h9pazq6zv7jqs8j4p5l", // public key
        AgePath = "age"
    },
    Samba = new SambaSettings
    {
        Enabled = false, // lub true: kopiuje .age (zaszyfrowany) po age
        SharePath = @"\\192.168.1.2\backups",
        DeleteSourceAfterCopy = true,
        CreateOkFile = true
    },
    Vps = new VpsSettings
    {
        Enabled = true,
        Host = "192.168.1.100",
        Port = 22,
        Username = "tomasz",
        PrivateKeyPath = @"C:\Users\tomasz\.ssh\id_ed25519",
        RemotePath = "/mnt/backups",
        DeleteSourceAfterCopy = true // po udanym SCP usuwa .age/.7z/.bak z OutputDirectory
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
| GET | `/api/backupjobs` | Lista (filtry: environment, instance, status, take) + stale-check (10 min -> `Failed`) |
| GET | `/api/backupjobs/active` | Tylko `Running` + stale-check |
| GET | `/api/backupjobs/{id}` | Pojedynczy job |
| POST | `/api/backupjobs` | Utwórz (broadcast `JobCreated` + `JobUpdated`) |
| PUT | `/api/backupjobs/{id}` | Aktualizuj (broadcast `JobUpdated`/`JobFinished`) |
| DELETE | `/api/backupjobs/{id}` | Usuń |

> **Stale-check**: `GET` automatycznie oznacza `Running` bez aktualizacji > `JobSettings:StaleMinutes` (10 min) jako `Failed` + `JobFinished` broadcast. Konsola dodatkowo finalizuje `Failed` w `catch` przy nieoczekiwanym wyjątku (try/finally). Podwójna ochrona przed "wiszącymi" zadaniami.

### BackupRecord Model
- EnvironmentName, InstanceName, DatabaseName, BackupType
- OutputFilePath, FileSize, BackupDate
- Compress, Verify, Duration

### BackupJob Model (live)
- EnvironmentName, InstanceName, HostName, Status (Running/Completed/Failed), BackupType (Full/Diff)
- StartedAt, FinishedAt, UpdatedAt (UTC)
- TotalDatabases, CompletedCount, FailedCount
- CurrentDatabase, CurrentStep, Message
- Web: `Jobs.razor` wyświetla `BackupType` jako badge (Full=primary, Diff=warning) w kartach aktywnych + kolumnie w tabeli historii z filtrem Typ

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
# Wszystkie skonfigurowane serwery (typ z configu: BackupSettings:DefaultType)
dotnet run --project src/MssqlBackup.Console

# Typ backupu dla calej operacji (Full/Diff, wspolny dla wszystkich baz/serwerow)
dotnet run --project src/MssqlBackup.Console -- --type Full
dotnet run --project src/MssqlBackup.Console -- --type Diff

# Konkretny serwer (Name lub Server)
dotnet run --project src/MssqlBackup.Console -- --server PROD-01
dotnet run --project src/MssqlBackup.Console -- --server ".\SQLEXPRESS"

# Kombinacja: serwer + typ
dotnet run --project src/MssqlBackup.Console -- --server PROD-01 --type Differential

# Sync katalogu baz (bez backupu) - wszystkie lub wskazany serwer
dotnet run --project src/MssqlBackup.Console -- --sync-databases
dotnet run --project src/MssqlBackup.Console -- --sync-databases PROD-01

# Test polaczenia z VPS (SCP) - wysyla pojedynczy plik testowy, nie wymaga wlaczania Vps:Enabled
dotnet run --project src/MssqlBackup.Console -- --test-vps
dotnet run --project src/MssqlBackup.Console -- --test-vps C:\temp\plik_testowy.txt
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
  },
  "JobSettings": {
    "StaleMinutes": 10
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
  },
  "JobSettings": {
    "StaleMinutes": 10
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
  },
  "LocalCopySettings": {
    "Enabled": false,
    "DestinationPath": "D:\\BackupsMirror\\MSSQL",
    "DeleteSourceAfterCopy": false
  },
  "AgeSettings": {
    "Enabled": false,
    "Recipient": "age1ql3z7hj432v2jl2z8alunwwun8ap59a43lfcw80h9pazq6zv7jqs8j4p5l",
    "RecipientsFile": null,
    "AgePath": "age"
  },
  "VpsSettings": {
    "Enabled": false,
    "Host": "192.168.1.100",
    "Port": 22,
    "Username": "tomasz",
    "PrivateKeyPath": "C:\\Users\\tomasz\\.ssh\\id_ed25519",
    "Password": null,
    "RemotePath": "/mnt/backups",
    "DeleteSourceAfterCopy": true
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

var localCopySettings = new LocalCopySettings();
configuration.GetSection("LocalCopySettings").Bind(localCopySettings);

var ageSettings = new AgeSettings();
configuration.GetSection("AgeSettings").Bind(ageSettings);

var vpsSettings = new VpsSettings();
configuration.GetSection("VpsSettings").Bind(vpsSettings);
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
- Pliki backupów są zapisywane w `[OutputDirectory]/[EnvironmentName]/[ServerName]/[yyyy-MM-dd HH-mm-ss_Full|_Diff]/` (ta sama struktura lokalnie, na Sambie i w LocalCopy); suffix `_Full`/`_Diff` zgodny z `--type` dla calej operacji
- Kolejność: `BACKUP DATABASE` -> kompresja 7-Zip -> kopiowanie (Samba/LocalCopy na końcu, oba po kompresji); `--type` (Full/Differential) wspólny dla wszystkich baz/serwerów, domyślnie z `BackupSettings:DefaultType`
- Flow z age/VPS: `BACKUP` -> 7zip (bez hasla) -> USB (LocalCopy, przed age, niezaszyfrowany .7z) -> age -r (szyfruje .7z -> .7z.age) -> VPS/Samba (zaszyfrowany .age, po age) -> delete .bak/.7z/.age z OutputDirectory (o ile Vps/Samba DeleteSourceAfterCopy); struktura folderu `yyyy-MM-dd HH-mm-ss_Full/_Diff` ta sama lokalnie, na Sambie, w LocalCopy i na VPS (`RemotePath/ENV/Server/folder/`)
- Age: wymaga binarki `age` (https://github.com/FiloSottile/age/releases) na Windows w PATH lub `AgeSettings:AgePath`; VPS Ubuntu: `sudo apt install age`; klucz publiczny `age1...` w `AgeSettings:Recipient`
- VPS: wymaga `scp` (OpenSSH C:\Windows\System32\OpenSSH\scp.exe) lub `pscp` (PuTTY) + `ssh`/`plink` dla `mkdir -p`; Ubuntu VPS: `sudo apt install openssh-server`, katalog `RemotePath` (np. `/mnt/backups`) + klucz SSH `C:\Users\tomasz\.ssh\id_ed25519` -> `ssh-copy-id` na VPS; auth via `VpsSettings:PrivateKeyPath` (OpenSSH format), BatchMode=yes
- Wiszące zadania: podwójna ochrona - konsola finalizuje `Failed` w `catch` (outer try w Orchestrator + per-server try w Program), API stale-check `JobSettings:StaleMinutes` (10 min bez `UpdatedAt` -> `Failed` + broadcast `JobFinished`); Web badge >5 min (warning), >10 min (danger)
- Błędy podczas backupu pojedynczych baz są logowane, a operacja jest kontynuowana
- W Dockerze API nasłuchuje na porcie 5000 (HTTP)
- SQL Server w Dockerze używa domyślnie hasła z pliku .env
- Deploy na NAS: `./deploy-to-nas.ps1` (wymaga PuTTY - plink/pscp)
