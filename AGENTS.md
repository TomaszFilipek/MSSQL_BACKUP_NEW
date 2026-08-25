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
├── MSSQL_BACKUP_NEW.sln
├── .gitignore
├── README.md
└── src/
    ├── MssqlBackup.Shared/           # Współdzielona biblioteka (.NET 9 class library)
    │   ├── MssqlBackup.Shared.csproj
    │   ├── Models/                   # Wspólne modele (DTO, encje)
    │   └── Interfaces/               # Wspólne interfejsy
    ├── MssqlBackup.Api/              # REST API (.NET 9 web API)
    │   ├── MssqlBackup.Api.csproj
    │   ├── Program.cs
    │   ├── Controllers/
    │   │   └── BackupRecordsController.cs  # CRUD dla historii backupów
    │   ├── Data/
    │   │   ├── AppDbContext.cs       # EF Core DbContext
    │   │   └── Migrations/           # Migracje EF Core
    │   ├── Models/
    │   │   └── BackupRecord.cs       # Encja historii backupów
    │   ├── appsettings.json
    │   └── appsettings.Development.json
    └── MssqlBackup.Console/          # Aplikacja konsolowa (.NET 9 console)
        ├── MssqlBackup.Console.csproj
        ├── Program.cs
        ├── appsettings.json           # Konfiguracja API, serwera, backupu, kompresji, Samba
        ├── Models/
        │   ├── BackupType.cs          # enum: Full, Differential
        │   ├── BackupOptions.cs       # Parametry backupu (DatabaseName, OutputPath, Type, Compress, Verify)
        │   ├── BackupConfiguration.cs # Konfiguracja orchestratora (OutputDirectory, ExcludeDatabases, Compress, Verify, Samba)
        │   ├── BackupSettings.cs      # Ustawienia backupu z appsettings.json
        │   ├── ServerConnection.cs    # Dane połączenia z serwerem SQL
        │   ├── ServerSettings.cs      # Ustawienia serwera SQL z appsettings.json
        │   ├── BackupResult.cs        # Wynik operacji backupu
        │   ├── BackupError.cs         # Informacja o błędzie
        │   ├── ApiSettings.cs         # Ustawienia API (BaseUrl, EnvironmentName)
        │   ├── BackupRecordDto.cs     # DTO do komunikacji z API
        │   ├── CompressionSettings.cs # Ustawienia kompresji (Compress, Password, CompressionLevel)
        │   └── SambaSettings.cs       # Ustawienia Samba (Enabled, SharePath, DeleteSourceAfterCopy, CreateOkFile)
        └── Services/
            ├── BackupService.cs       # Wykonywanie backupów (BACKUP DATABASE)
            ├── BackupOrchestrator.cs  # Orchestration backupu wszystkich baz
            ├── BackupApiClient.cs     # Klient HTTP do wysyłania rekordów do API
            ├── CompressionService.cs  # Kompresja plików 7-Zip (z obsługą hasła)
            └── SambaService.cs        # Wysyłka backupów na udziały sieciowe Samba
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
| MssqlBackup.Console | MssqlBackup.Shared | Microsoft.Extensions.Configuration, Microsoft.Extensions.Configuration.Json, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging.Console, Microsoft.Extensions.Http, Microsoft.Data.SqlClient |
| MssqlBackup.Api | MssqlBackup.Shared | Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Sqlite, Microsoft.EntityFrameworkCore.Design, Scalar.AspNetCore |

## Console App Architecture

### Kluczowe klasy
- **BackupService** - wykonuje backupy pojedynczych baz (BACKUP DATABASE)
- **BackupOrchestrator** - orchestruje backup wszystkich baz na serwerze
- **BackupApiClient** - klient HTTP do wysyłania rekordów do REST API
- **CompressionService** - kompresja plików 7-Zip (z obsługą hasła)
- **SambaService** - wysyłka backupów na udziały sieciowe Samba
- **ServerConnection** - dane połączenia (Server, Username, Password, UseWindowsAuth)
- **BackupConfiguration** - konfiguracja orchestratora (OutputDirectory, ExcludeDatabases, Compress, Verify)
- **BackupOptions** - parametry backupu (DatabaseName, OutputPath, Type, Compress, Verify)
- **BackupResult** - wynik operacji (TotalDatabases, SuccessfulBackups, FailedBackups, Errors)
- **ApiSettings** - ustawienia API (BaseUrl, EnvironmentName)
- **CompressionSettings** - ustawienia kompresji (Compress, Password, CompressionLevel)
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
        CompressionLevel = "Normal"
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
| POST | `/api/backuprecords` | Utwórz |
| PUT | `/api/backuprecords/{id}` | Aktualizuj |
| DELETE | `/api/backuprecords/{id}` | Usuń |

### BackupRecord Model
- EnvironmentName, InstanceName, DatabaseName, BackupType
- OutputFilePath, FileSize, BackupDate
- Compress, Verify, Duration

### Konfiguracja
- **CORS**: AllowAnyOrigin (dla instancji konsolowych na VPS)
- **AutoMigrate**: Automatyczna migracja przy starcie
- **API Documentation**: Scalar (zamiast Swagger UI) - dostępne pod `/scalar/v1` w trybie Development

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
dotnet run --project src/MssqlBackup.Console
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
  "ServerSettings": {
    "Server": ".\\SQLEXPRESS",
    "Database": null,
    "Username": null,
    "Password": null,
    "UseWindowsAuth": true
  },
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
    "CompressionLevel": "Normal"
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

### Console - `Program.cs` (odczyt konfiguracji z appsettings)
```csharp
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
```

## Docker

### Struktura plików Docker
```
MSSQL_BACKUP_NEW/
├── Dockerfile              # Multi-stage build dla API
├── docker-compose.yml      # API + wolumen dla SQLite
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
- Pliki backupów są zapisywane w podkatalogach datowych (yyyy-MM-dd/)
- Błędy podczas backupu pojedynczych baz są logowane, a operacja jest kontynuowana
- W Dockerze API nasłuchuje na porcie 5000 (HTTP)
- SQL Server w Dockerze używa domyślnie hasła z pliku .env
- Deploy na NAS: `./deploy-to-nas.ps1` (wymaga PuTTY - plink/pscp)
