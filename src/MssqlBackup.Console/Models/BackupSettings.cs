namespace MssqlBackup.Console.Models;

public class BackupSettings
{
    public string OutputDirectory { get; set; } = @"C:\Backups\MSSQL";
    public BackupType DefaultType { get; set; } = BackupType.Full;
    public bool Compress { get; set; }
    public bool Verify { get; set; }
    public List<string> ExcludeDatabases { get; set; } =
    [
        "master",
        "model",
        "msdb",
        "tempdb"
    ];
}
