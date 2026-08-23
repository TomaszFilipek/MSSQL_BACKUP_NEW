namespace MssqlBackup.Console.Models;

public class BackupConfiguration
{
    public required string OutputDirectory { get; init; }
    public BackupType DefaultType { get; init; } = BackupType.Full;
    public bool Compress { get; init; }
    public bool Verify { get; init; }
    public List<string> ExcludeDatabases { get; init; } =
    [
        "master",
        "model",
        "msdb",
        "tempdb"
    ];
    public CompressionSettings PostBackupCompression { get; init; } = new();
    public SambaSettings Samba { get; init; } = new();
}
