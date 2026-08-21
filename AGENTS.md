# AGENTS.md

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
    │   ├── Controllers/              # Kontrolery API
    │   ├── Data/
    │   │   ├── AppDbContext.cs       # EF Core DbContext
    │   │   └── Migrations/           # Migracje EF Core
    │   ├── appsettings.json
    │   └── appsettings.Development.json
    └── MssqlBackup.Console/          # Aplikacja konsolowa (.NET 9 console)
        ├── MssqlBackup.Console.csproj
        └── Program.cs
```

## Key Technical Decisions

- **Framework**: .NET 9 (STS)
- **ORM**: Entity Framework Core 9.x (dla REST API)
- **Baza danych**: SQL Server (EntityFrameworkCore.SqlServer)
- **Namespace**: MssqlBackup.*
- **API**: Kontrollery (nie Minimal API)
- **Konfiguracja**: appsettings.json + appsettings.Development.json

## Dependencies

| Project | References | NuGet Packages |
|---------|-----------|----------------|
| MssqlBackup.Shared | — | — |
| MssqlBackup.Console | MssqlBackup.Shared | Microsoft.Extensions.Configuration, Microsoft.Extensions.Configuration.Json, Microsoft.Extensions.DependencyInjection |
| MssqlBackup.Api | MssqlBackup.Shared | Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Design |

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

## Notes

- Aplikacja konsolowa korzysta z innej bazy danych niż REST API
- Migracje EF Core znajdują się w projekcie API (Data/Migrations/)
- Połączenie z bazą danych jest konfigurowane przez appsettings.json
