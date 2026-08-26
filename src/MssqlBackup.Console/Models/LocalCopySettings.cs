namespace MssqlBackup.Console.Models;

public class LocalCopySettings
{
    public bool Enabled { get; set; }
    public string DestinationPath { get; set; } = string.Empty;
    public bool DeleteSourceAfterCopy { get; set; }
}
