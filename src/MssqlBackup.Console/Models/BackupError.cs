namespace MssqlBackup.Console.Models;

public class BackupError
{
    public required string DatabaseName { get; init; }
    public required string ErrorMessage { get; init; }
}
