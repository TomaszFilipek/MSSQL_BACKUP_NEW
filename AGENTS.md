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
        ├── appsettings.json           # Konfiguracja API (BaseUrl, EnvironmentName)
        ├── Models/
        │   ├── BackupType.cs          # enum: Full, Differential
        │   ├── BackupOptions.cs       # Parametry backupu (DatabaseName, OutputPath, Type, Compress, Verify)
        │   ├── BackupConfiguration.cs # Konfiguracja orchestratora (OutputDirectory, ExcludeDatabases)
        │   ├── ServerConnection.cs    # Dane połączenia z serwerem SQL
        │   ├── BackupResult.cs        # Wynik operacji backupu
        │   ├── BackupError.cs         # Informacja o błędzie
        │   ├── ApiSettings.cs         # Ustawienia API (BaseUrl, EnvironmentName)
        │   ├── BackupRecordDto.cs     # DTO do komunikacji z API
        │   └── CompressionSettings.cs # Ustawienia kompresji (Compress, Password, CompressionLevel)
        └── Services/
            ├── BackupService.cs       # Wykonywanie backupów (BACKUP DATABASE)
            ├── BackupOrchestrator.cs  # Orchestration backupu wszystkich baz
            ├── BackupApiClient.cs     # Klient HTTP do wysyłania rekordów do API
            └── CompressionService.cs  # Kompresja plików 7-Zip (z obsługą hasła)
```

## Key Technical Decisions

- **Framework**: .NET 9 (STS)
- **ORM**: Entity Framework Core 9.x (dla REST API)
- **Baza danych**: SQL Server (EntityFrameworkCore.SqlServer)
- **Namespace**: MssqlBackup.*
- **API**: Kontrollery (nie Minimal API)
- **Konfiguracja**: appsettings.json + appsettings.Development.json
- **Backup SQL**: Microsoft.Data.SqlClient + BACKUP DATABASE

## Dependencies

| Project | References | NuGet Packages |
|---------|-----------|----------------|
| MssqlBackup.Shared | — | — |
| MssqlBackup.Console | MssqlBackup.Shared | Microsoft.Extensions.Configuration, Microsoft.Extensions.Configuration.Json, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging.Console, Microsoft.Extensions.Http, Microsoft.Data.SqlClient |
| MssqlBackup.Api | MssqlBackup.Shared | Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Design, Scalar.AspNetCore |

## Console App Architecture

### Kluczowe klasy
- **BackupService** - wykonuje backupy pojedynczych baz (BACKUP DATABASE)
- **BackupOrchestrator** - orchestruje backup wszystkich baz na serwerze
- **BackupApiClient** - klient HTTP do wysyłania rekordów do REST API
- **CompressionService** - kompresja plików 7-Zip (z obsługą hasła)
- **ServerConnection** - dane połączenia (Server, Username, Password, UseWindowsAuth)
- **BackupConfiguration** - konfiguracja orchestratora (OutputDirectory, ExcludeDatabases, Compress, Verify)
- **BackupOptions** - parametry backupu (DatabaseName, OutputPath, Type, Compress, Verify)
- **BackupResult** - wynik operacji (TotalDatabases, SuccessfulBackups, FailedBackups, Errors)
- **ApiSettings** - ustawienia API (BaseUrl, EnvironmentName)
- **CompressionSettings** - ustawienia kompresji (Compress, Password, CompressionLevel)

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

## Docker

### Struktura plików Docker
```
MSSQL_BACKUP_NEW/
├── Dockerfile              # Multi-stage build dla API
├── docker-compose.yml      # API + SQL Server
├── .dockerignore           # Wykluczenia z kontekstu buildu
└── .env                    # Zmienne środowiskowe (SA_PASSWORD)
```

### Uruchomienie na VPS
```bash
# 1. Sklonuj repozytorium
git clone <repo-url>
cd MSSQL_BACKUP_NEW

# 2. Utwórz plik .env z hasłem SA
echo "SA_PASSWORD=YourStrong!Password123" > .env

# 3. Zbuduj i uruchom
docker compose up -d --build

# 4. Sprawdź status
docker compose ps
docker compose logs api
```

### Endpoints po uruchomieniu
- API: `http://<VPS_IP>:5000`
- Scalar API Docs: `http://<VPS_IP>:5000/scalar/v1` (w trybie Development)
- SQL Server: `localhost:1433` (zewnętrznie)

### Zarządzanie
```bash
# Zatrzymanie
docker compose down

# Usunięcie z danymi
docker compose down -v

# Logi
docker compose logs -f api
docker compose logs -f mssql
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
