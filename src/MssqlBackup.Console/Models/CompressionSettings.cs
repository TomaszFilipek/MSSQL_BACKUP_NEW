namespace MssqlBackup.Console.Models;

public class CompressionSettings
{
    public bool Compress { get; set; }
    public string? Password { get; set; }
    public string CompressionLevel { get; set; } = "Normal";
}
