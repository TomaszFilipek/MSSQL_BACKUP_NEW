namespace MssqlBackup.Console.Models;

public class BackupOptions
{
    public required string DatabaseName { get; init; }
    public required string OutputPath { get; init; }
    public BackupType Type { get; init; } = BackupType.Full;
    public bool Compress { get; init; }
    public bool Verify { get; init; }
}
