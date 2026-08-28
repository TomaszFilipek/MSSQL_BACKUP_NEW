namespace MssqlBackup.Console.Models;

public class AgeSettings
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Public recipient key for age (age1...). Used as -r argument.
    /// Required when Enabled = true (unless RecipientsFile is set).
    /// </summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>
    /// Optional path to file containing recipient keys (one per line).
    /// When set, passed as --recipients-file.
    /// </summary>
    public string? RecipientsFile { get; set; }

    /// <summary>
    /// Optional custom path to age binary. Default "age" (PATH).
    /// Supports C:\Program Files\age\age.exe, /usr/bin/age etc.
    /// </summary>
    public string AgePath { get; set; } = "age";
}
