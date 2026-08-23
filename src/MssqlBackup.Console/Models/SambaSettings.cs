namespace MssqlBackup.Console.Models;

public class SambaSettings
{
    public bool Enabled { get; set; }
    public string SharePath { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Domain { get; set; }
    public bool DeleteSourceAfterCopy { get; set; }
    public bool CreateOkFile { get; set; }
}
