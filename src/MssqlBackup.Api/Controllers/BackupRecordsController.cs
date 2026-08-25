using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using MssqlBackup.Api.Data;
using MssqlBackup.Api.Hubs;
using MssqlBackup.Api.Models;

namespace MssqlBackup.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BackupRecordsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<BackupHub> _hubContext;

    public BackupRecordsController(AppDbContext context, IHubContext<BackupHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BackupRecord>>> GetBackupRecords(
        [FromQuery] string? environment = null,
        [FromQuery] string? instance = null,
        [FromQuery] string? database = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var query = _context.BackupRecords.AsQueryable();

        if (!string.IsNullOrEmpty(environment))
            query = query.Where(r => r.EnvironmentName == environment);

        if (!string.IsNullOrEmpty(instance))
            query = query.Where(r => r.InstanceName == instance);

        if (!string.IsNullOrEmpty(database))
            query = query.Where(r => r.DatabaseName == database);

        if (from.HasValue)
            query = query.Where(r => r.BackupDate >= from.Value);

        if (to.HasValue)
            query = query.Where(r => r.BackupDate <= to.Value);

        return await query.OrderByDescending(r => r.BackupDate).ToListAsync();
    }

    [HttpGet("latest")]
    public async Task<ActionResult<IEnumerable<BackupRecord>>> GetLatestBackups(
        [FromQuery] string? environment = null)
    {
        var query = _context.BackupRecords.AsQueryable();

        if (!string.IsNullOrEmpty(environment))
            query = query.Where(r => r.EnvironmentName == environment);

        var latestRecords = await query
            .GroupBy(r => new { r.EnvironmentName, r.DatabaseName })
            .Select(g => g.OrderByDescending(r => r.BackupDate).First())
            .ToListAsync();

        return Ok(latestRecords);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BackupRecord>> GetBackupRecord(int id)
    {
        var record = await _context.BackupRecords.FindAsync(id);

        if (record == null)
            return NotFound();

        return record;
    }

    [HttpPost]
    public async Task<ActionResult<BackupRecord>> CreateBackupRecord(BackupRecord record)
    {
        _context.BackupRecords.Add(record);
        await _context.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("BackupCreated", record);

        return CreatedAtAction(nameof(GetBackupRecord), new { id = record.Id }, record);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBackupRecord(int id, BackupRecord record)
    {
        if (id != record.Id)
            return BadRequest();

        _context.Entry(record).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.BackupRecords.AnyAsync(e => e.Id == id))
                return NotFound();
            throw;
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBackupRecord(int id)
    {
        var record = await _context.BackupRecords.FindAsync(id);

        if (record == null)
            return NotFound();

        _context.BackupRecords.Remove(record);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
