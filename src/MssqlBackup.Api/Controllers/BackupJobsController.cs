using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MssqlBackup.Api.Data;
using MssqlBackup.Api.Hubs;
using MssqlBackup.Api.Models;

namespace MssqlBackup.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BackupJobsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<BackupHub> _hubContext;
    private readonly IConfiguration _configuration;

    public BackupJobsController(AppDbContext context, IHubContext<BackupHub> hubContext, IConfiguration configuration)
    {
        _context = context;
        _hubContext = hubContext;
        _configuration = configuration;
    }

    private async Task CheckAndMarkStaleJobs()
    {
        var staleMinutes = _configuration.GetValue<int>("JobSettings:StaleMinutes", 10);
        var threshold = DateTime.UtcNow.AddMinutes(-staleMinutes);
        var stale = await _context.BackupJobs
            .Where(j => j.Status == "Running" && j.UpdatedAt < threshold)
            .ToListAsync();

        foreach (var job in stale)
        {
            job.Status = "Failed";
            job.FinishedAt = DateTime.UtcNow;
            job.CurrentStep = "Abandoned";
            job.Message = $"Przerwane - brak raportowania > {staleMinutes} min (stale)";
            job.UpdatedAt = DateTime.UtcNow;
        }

        if (stale.Count > 0)
        {
            await _context.SaveChangesAsync();
            foreach (var job in stale)
            {
                await _hubContext.Clients.All.SendAsync("JobFinished", job);
            }
        }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BackupJob>>> GetJobs(
        [FromQuery] string? environment = null,
        [FromQuery] string? instance = null,
        [FromQuery] string? status = null,
        [FromQuery] int take = 50)
    {
        await CheckAndMarkStaleJobs();

        var query = _context.BackupJobs.AsQueryable();

        if (!string.IsNullOrEmpty(environment))
            query = query.Where(j => j.EnvironmentName == environment);
        if (!string.IsNullOrEmpty(instance))
            query = query.Where(j => j.InstanceName == instance);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(j => j.Status == status);

        return await query.OrderByDescending(j => j.UpdatedAt).Take(take).ToListAsync();
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<BackupJob>>> GetActiveJobs()
    {
        await CheckAndMarkStaleJobs();

        var active = await _context.BackupJobs
            .Where(j => j.Status == "Running")
            .OrderByDescending(j => j.StartedAt)
            .ToListAsync();
        return Ok(active);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BackupJob>> GetJob(Guid id)
    {
        var job = await _context.BackupJobs.FindAsync(id);
        if (job == null) return NotFound();
        return job;
    }

    [HttpPost]
    public async Task<ActionResult<BackupJob>> CreateJob(BackupJob job)
    {
        if (job.Id == Guid.Empty)
            job.Id = Guid.NewGuid();
        job.StartedAt = job.StartedAt == default ? DateTime.UtcNow : DateTime.SpecifyKind(job.StartedAt, DateTimeKind.Utc);
        job.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrEmpty(job.Status))
            job.Status = "Running";

        _context.BackupJobs.Add(job);
        await _context.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("JobCreated", job);
        await _hubContext.Clients.All.SendAsync("JobUpdated", job);

        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateJob(Guid id, BackupJob job)
    {
        if (id != job.Id) return BadRequest();

        var existing = await _context.BackupJobs.FindAsync(id);
        if (existing == null) return NotFound();

        existing.EnvironmentName = job.EnvironmentName;
        existing.InstanceName = job.InstanceName;
        existing.HostName = job.HostName;
        existing.Status = job.Status;
        existing.BackupType = string.IsNullOrWhiteSpace(job.BackupType) ? existing.BackupType : job.BackupType;
        existing.FinishedAt = job.FinishedAt.HasValue ? DateTime.SpecifyKind(job.FinishedAt.Value, DateTimeKind.Utc) : null;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.TotalDatabases = job.TotalDatabases;
        existing.CompletedCount = job.CompletedCount;
        existing.FailedCount = job.FailedCount;
        existing.CurrentDatabase = job.CurrentDatabase;
        existing.CurrentStep = job.CurrentStep;
        existing.Message = job.Message;
        existing.ServerName = job.ServerName;
        existing.TotalServers = job.TotalServers;
        existing.ServerIndex = job.ServerIndex;
        existing.Databases = job.Databases ?? [];

        await _context.SaveChangesAsync();

        if (job.Status == "Running")
            await _hubContext.Clients.All.SendAsync("JobUpdated", existing);
        else
            await _hubContext.Clients.All.SendAsync("JobFinished", existing);

        // keep history - optionally cleanup old finished jobs >30 days
        // not deleting automatically here

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteJob(Guid id)
    {
        var job = await _context.BackupJobs.FindAsync(id);
        if (job == null) return NotFound();
        _context.BackupJobs.Remove(job);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
