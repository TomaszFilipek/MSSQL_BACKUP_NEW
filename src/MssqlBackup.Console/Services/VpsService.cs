using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class VpsService
{
    private readonly ILogger<VpsService> _logger;

    public VpsService(ILogger<VpsService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Copies encrypted file to VPS via SCP (OpenSSH scp or PuTTY pscp).
    /// Creates remote directory via ssh/plink mkdir -p before copy.
    /// </summary>
    public async Task CopyToVpsAsync(string sourceFilePath, string remoteFilePath, VpsSettings settings)
    {
        if (!settings.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(settings.Host))
            throw new InvalidOperationException("VpsSettings:Host is required when Vps copy is enabled");

        if (string.IsNullOrWhiteSpace(settings.Username))
            throw new InvalidOperationException("VpsSettings:Username is required when Vps copy is enabled");

        if (string.IsNullOrWhiteSpace(settings.RemotePath))
            throw new InvalidOperationException("VpsSettings:RemotePath is required when Vps copy is enabled");

        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source file for VPS copy not found: {sourceFilePath}");

        if (string.IsNullOrWhiteSpace(remoteFilePath))
            throw new ArgumentException("Remote file path is empty", nameof(remoteFilePath));

        var (scpPath, isPscp) = FindScpPath();
        if (string.IsNullOrEmpty(scpPath))
            throw new InvalidOperationException("SCP binary not found (scp/pscp). Install OpenSSH (C:\\Windows\\System32\\OpenSSH\\scp.exe) or PuTTY (pscp.exe) and ensure it is in PATH");

        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath) && !File.Exists(settings.PrivateKeyPath))
            throw new FileNotFoundException($"VPS private key not found: {settings.PrivateKeyPath}");

        // remoteFilePath is like /mnt/backups/ENV/Server/.../file.7z.age
        var remoteDir = GetRemoteDirectory(remoteFilePath);

        _logger.LogInformation("Ensuring remote directory '{RemoteDir}' on {User}@{Host}", remoteDir, settings.Username, settings.Host);
        await EnsureRemoteDirectoryAsync(remoteDir, settings, isPscp);

        _logger.LogInformation("Copying '{Source}' to VPS {User}@{Host}:{RemoteFile} via {Tool}",
            sourceFilePath, settings.Username, settings.Host, remoteFilePath, isPscp ? "pscp" : "scp");

        var scpArgs = BuildScpArguments(sourceFilePath, remoteFilePath, settings, isPscp);
        var scpExit = await RunProcessAsync(scpPath, scpArgs);

        if (scpExit != 0)
            throw new InvalidOperationException($"SCP to VPS failed with exit code {scpExit}. Check Host, Username, PrivateKeyPath, RemotePath and host key");

        _logger.LogInformation("VPS copy successful to {Host}:{RemoteFile}", settings.Host, remoteFilePath);
    }

    private async Task EnsureRemoteDirectoryAsync(string remoteDir, VpsSettings settings, bool isPscp)
    {
        var (sshPath, sshIsPlink) = FindSshPath(isPscp);

        // If ssh/plink not found, skip mkdir (SCP will fail if dir missing, user must create manually)
        if (string.IsNullOrEmpty(sshPath))
        {
            _logger.LogWarning("SSH/plink binary not found - skipping remote mkdir, relying on SCP. Create directory manually: mkdir -p '{RemoteDir}'", remoteDir);
            return;
        }

        var mkdirCmd = $"mkdir -p '{EscapeRemotePath(remoteDir)}'";
        var sshArgs = BuildSshArguments(mkdirCmd, settings, sshIsPlink);

        _logger.LogDebug("Creating remote dir via {Tool}: {Args}", sshIsPlink ? "plink" : "ssh", sshArgs);
        var exit = await RunProcessAsync(sshPath, sshArgs);

        if (exit != 0)
            _logger.LogWarning("Remote mkdir failed with exit {Exit} - SCP may fail if directory does not exist", exit);
    }

    private static string BuildScpArguments(string sourceFilePath, string remoteFilePath, VpsSettings settings, bool isPscp)
    {
        var userHost = $"{settings.Username}@{settings.Host}";
        // For SCP, remote quoting: user@host:'/path/with spaces/file'
        var remoteQuoted = $"{userHost}:'{EscapeRemotePath(remoteFilePath)}'";

        if (isPscp)
        {
            // pscp: pscp -batch -P port -i key "source" user@host:"/path"
            var args = "-batch";
            if (settings.Port != 22) args += $" -P {settings.Port}";
            if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath)) args += $" -i \"{settings.PrivateKeyPath}\"";
            // pscp needs quoted source on Windows
            args += $" \"{sourceFilePath}\" {remoteQuoted}";
            return args;
        }
        else
        {
            // OpenSSH scp: scp -P port -i key "source" user@host:'/path'
            var args = "";
            if (settings.Port != 22) args += $"-P {settings.Port} ";
            if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath)) args += $"-i \"{settings.PrivateKeyPath}\" ";
            // StrictHostKeyChecking disabled for automation (optional, but helpful)
            // Use BatchMode to avoid password prompts
            args += "-o BatchMode=yes ";
            // Use -O for legacy SCP protocol if needed? Not needed on newer OpenSSH
            args = args.Trim();
            if (!string.IsNullOrEmpty(args)) args += " ";
            args += $"\"{sourceFilePath}\" {remoteQuoted}";
            return args;
        }
    }

    private static string BuildSshArguments(string remoteCommand, VpsSettings settings, bool isPlink)
    {
        var userHost = $"{settings.Username}@{settings.Host}";
        if (isPlink)
        {
            // plink -batch -ssh -P port -i key user@host "mkdir -p '...'"
            var args = "-batch -ssh";
            if (settings.Port != 22) args += $" -P {settings.Port}";
            if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath)) args += $" -i \"{settings.PrivateKeyPath}\"";
            args += $" {userHost} \"{remoteCommand}\"";
            return args;
        }
        else
        {
            // ssh -p port -i key -o BatchMode=yes user@host "mkdir -p '...'"
            var args = "";
            if (settings.Port != 22) args += $"-p {settings.Port} ";
            if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath)) args += $"-i \"{settings.PrivateKeyPath}\" ";
            args += "-o BatchMode=yes ";
            args += $"{userHost} \"{remoteCommand}\"";
            return args;
        }
    }

    private static string GetRemoteDirectory(string remoteFilePath)
    {
        // remoteFilePath is Unix-style: /mnt/backups/.../file.age
        var idx = remoteFilePath.LastIndexOf('/');
        if (idx <= 0) return remoteFilePath;
        return remoteFilePath[..idx];
    }

    private static string EscapeRemotePath(string path)
    {
        // Escape single quotes for shell: ' -> '\'' 
        return path.Replace("'", "'\\''");
    }

    private async Task<int> RunProcessAsync(string fileName, string arguments)
    {
        _logger.LogDebug("Running: {File} {Args}", fileName, arguments);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(stdout))
            _logger.LogDebug("{File} stdout: {Output}", fileName, stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogDebug("{File} stderr: {Output}", fileName, stderr.Trim());

        if (process.ExitCode != 0)
            _logger.LogWarning("{File} exited with {Exit}: {Err} {Out}", fileName, process.ExitCode, stderr.Trim(), stdout.Trim());

        return process.ExitCode;
    }

    private static (string path, bool isPscp) FindScpPath()
    {
        var candidates = new[]
        {
            (@"C:\Windows\System32\OpenSSH\scp.exe", false),
            (@"C:\Program Files\PuTTY\pscp.exe", true),
            (@"C:\Program Files (x86)\PuTTY\pscp.exe", true),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", "pscp.exe"), true),
            (@"C:\tools\PuTTY\pscp.exe", true),
            ("scp", false),
            ("pscp", true),
        };

        foreach (var (path, isPscp) in candidates)
        {
            if (path == "scp" || path == "pscp") return (path, isPscp);
            if (File.Exists(path)) return (path, isPscp);
        }

        return ("scp", false);
    }

    private static (string path, bool isPlink) FindSshPath(bool preferPlink)
    {
        if (preferPlink)
        {
            var plinkCandidates = new[]
            {
                @"C:\Program Files\PuTTY\plink.exe",
                @"C:\Program Files (x86)\PuTTY\plink.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", "plink.exe"),
                "plink"
            };
            foreach (var p in plinkCandidates)
            {
                if (p == "plink") return (p, true);
                if (File.Exists(p)) return (p, true);
            }
        }

        var sshCandidates = new[]
        {
            @"C:\Windows\System32\OpenSSH\ssh.exe",
            "ssh"
        };
        foreach (var p in sshCandidates)
        {
            if (p == "ssh") return (p, false);
            if (File.Exists(p)) return (p, false);
        }

        return ("ssh", false);
    }
}
