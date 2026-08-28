namespace MssqlBackup.Console.Models;

public class VpsSettings
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Path to private SSH key (OpenSSH format). Used as -i argument for scp/ssh.
    /// Example: C:\Users\tomasz\.ssh\id_ed25519
    /// </summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional password when key is not used. Not recommended - use key auth.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Passphrase for private key if encrypted.
    /// Currently not used by scp wrapper (requires ssh-agent).
    /// </summary>
    public string? PrivateKeyPassphrase { get; set; }

    /// <summary>
    /// Remote base directory on VPS, e.g. /mnt/backups or /home/tomasz/backups.
    /// Final path will be: {RemotePath}/{Environment}/{Server}/{yyyy-MM-dd HH-mm-ss_Full|_Diff}/
    /// </summary>
    public string RemotePath { get; set; } = string.Empty;

    /// <summary>
    /// When true, delete local .age file after successful upload (and also intermediate .7z/.bak).
    /// Step 6 of flow: usunięcie age, 7zip, bak
    /// </summary>
    public bool DeleteSourceAfterCopy { get; set; } = true;
}
