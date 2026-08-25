namespace MssqlBackup.Console.Models;

public class NamedServerSettings
{
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = @".\SQLEXPRESS";
    public string? Database { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseWindowsAuth { get; set; } = true;

    public ServerConnection ToConnection() => new ServerConnection
    {
        Server = Server,
        Database = Database,
        Username = Username,
        Password = Password,
        UseWindowsAuth = UseWindowsAuth
    };
}
