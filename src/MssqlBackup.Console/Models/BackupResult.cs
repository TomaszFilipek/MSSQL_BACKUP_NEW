namespace MssqlBackup.Console.Models;

public class BackupResult
{
    public int TotalDatabases { get; set; }
    public int SuccessfulBackups { get; set; }
    public int FailedBackups { get; set; }
    public List<BackupError> Errors { get; set; } = [];
}
