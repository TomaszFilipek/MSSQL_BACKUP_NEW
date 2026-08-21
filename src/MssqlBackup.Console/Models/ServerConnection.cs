namespace MssqlBackup.Console.Models;

public class ServerConnection
{
    public required string Server { get; init; }
    public string? Database { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseWindowsAuth { get; init; } = true;
}
