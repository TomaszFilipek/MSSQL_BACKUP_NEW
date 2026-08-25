namespace MssqlBackup.Console.Models;

public class ServerSettings
{
    public string Server { get; set; } = @".\SQLEXPRESS";
    public string? Database { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseWindowsAuth { get; set; } = true;
}
