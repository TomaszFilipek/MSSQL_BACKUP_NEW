using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MssqlBackup.Api.Data;
using MssqlBackup.Api.Models;

namespace MssqlBackup.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DatabasesController : ControllerBase
{
    private readonly AppDbContext _context;

    public DatabasesController(AppDbContext context)
    {
        _context = context;
    }

    private static string BuildKey(string env, string instance, string db)
        => $"{env.Trim().ToLowerInvariant()}|{instance.Trim().ToLowerInvariant()}|{db.Trim().ToLowerInvariant()}";

    [HttpPost("sync")]
    public async Task<ActionResult> Sync([FromBody] DatabaseSyncRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EnvironmentName) || string.IsNullOrWhiteSpace(request.InstanceName))
            return BadRequest("EnvironmentName and InstanceName required");

        var now = DateTime.UtcNow;
        var incomingKeys = new HashSet<string>();

        foreach (var dbName in request.DatabaseNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(dbName)) continue;
            var key = BuildKey(request.EnvironmentName, request.InstanceName, dbName);
            incomingKeys.Add(key);

            var existing = await _context.RegisteredDatabases.FirstOrDefaultAsync(d => d.DatabaseKey == key);
            if (existing == null)
            {
                _context.RegisteredDatabases.Add(new RegisteredDatabase
                {
                    EnvironmentName = request.EnvironmentName,
                    InstanceName = request.InstanceName,
                    ServerName = request.ServerName ?? string.Empty,
                    DatabaseName = dbName,
                    DatabaseKey = key,
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastSeenAt = now,
                    IsActive = true
                });
            }
            else
            {
                existing.ServerName = request.ServerName ?? existing.ServerName;
                existing.DatabaseName = dbName; // preserve case
                existing.UpdatedAt = now;
                existing.LastSeenAt = now;
                existing.IsActive = true;
            }
        }

        // Mark missing as inactive (those previously active for this env/instance not in incoming list)
        var existingForInstance = await _context.RegisteredDatabases
            .Where(d => d.EnvironmentName == request.EnvironmentName && d.InstanceName == request.InstanceName)
            .ToListAsync();

        foreach (var db in existingForInstance)
        {
            if (!incomingKeys.Contains(db.DatabaseKey))
            {
                db.IsActive = false;
                db.UpdatedAt = now;
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { synced = incomingKeys.Count, total = existingForInstance.Count });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DatabaseWithBackupDto>>> GetDatabases(
        [FromQuery] string? environment = null,
        [FromQuery] string? instance = null,
        [FromQuery] string? server = null,
        [FromQuery] string? database = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool desc = false,
        [FromQuery] int take = 200)
    {
        var query = _context.RegisteredDatabases.AsQueryable();

        if (!string.IsNullOrEmpty(environment))
            query = query.Where(d => d.EnvironmentName == environment);
        if (!string.IsNullOrEmpty(instance))
            query = query.Where(d => d.InstanceName == instance);
        if (!string.IsNullOrEmpty(server))
            query = query.Where(d => d.ServerName == server);
        if (!string.IsNullOrEmpty(database))
            query = query.Where(d => d.DatabaseName.Contains(database));
        if (isActive.HasValue)
            query = query.Where(d => d.IsActive == isActive.Value);

        var dbs = await query.OrderBy(d => d.DatabaseName).Take(1000).ToListAsync();

        // Fetch latest backup per DatabaseKey via grouping over BackupRecords
        // We need to map BackupRecords to key same way
        var envFilter = environment;
        var latestMap = await _context.BackupRecords
            .Where(r => envFilter == null || r.EnvironmentName == envFilter)
            .GroupBy(r => r.EnvironmentName.ToLower() + "|" + r.InstanceName.ToLower() + "|" + r.DatabaseName.ToLower())
            .Select(g => g.OrderByDescending(r => r.BackupDate).First())
            .ToListAsync();

        var latestDict = latestMap.ToDictionary(
            r => (r.EnvironmentName + "|" + r.InstanceName + "|" + r.DatabaseName).ToLowerInvariant(),
            r => r);

        var result = dbs.Select(d =>
        {
            latestDict.TryGetValue(d.DatabaseKey, out var last);
            return new DatabaseWithBackupDto
            {
                Id = d.Id,
                EnvironmentName = d.EnvironmentName,
                InstanceName = d.InstanceName,
                ServerName = d.ServerName,
                DatabaseName = d.DatabaseName,
                DatabaseKey = d.DatabaseKey,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                LastSeenAt = d.LastSeenAt,
                IsActive = d.IsActive,
                LastBackupDate = last?.BackupDate,
                LastBackupType = last?.BackupType,
                LastFileSize = last?.FileSize,
                LastDuration = last?.Duration,
                LastBackupId = last?.Id
            };
        });

        // Sorting
        result = sortBy?.ToLower() switch
        {
            "database" => desc ? result.OrderByDescending(r => r.DatabaseName) : result.OrderBy(r => r.DatabaseName),
            "environment" => desc ? result.OrderByDescending(r => r.EnvironmentName) : result.OrderBy(r => r.EnvironmentName),
            "instance" => desc ? result.OrderByDescending(r => r.InstanceName) : result.OrderBy(r => r.InstanceName),
            "server" => desc ? result.OrderByDescending(r => r.ServerName) : result.OrderBy(r => r.ServerName),
            "lastbackup" => desc ? result.OrderByDescending(r => r.LastBackupDate) : result.OrderBy(r => r.LastBackupDate),
            _ => result.OrderBy(r => r.DatabaseName)
        };

        return Ok(result.Take(take).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RegisteredDatabase>> GetDatabase(int id)
    {
        var db = await _context.RegisteredDatabases.FindAsync(id);
        if (db == null) return NotFound();
        return db;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDatabase(int id)
    {
        var db = await _context.RegisteredDatabases.FindAsync(id);
        if (db == null) return NotFound();
        _context.RegisteredDatabases.Remove(db);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
