using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class SambaService
{
    private readonly ILogger<SambaService> _logger;

    public SambaService(ILogger<SambaService> logger)
    {
        _logger = logger;
    }

    public async Task CopyToShareAsync(string sourceFilePath, SambaSettings settings)
    {
        if (!settings.Enabled || string.IsNullOrEmpty(settings.SharePath))
            return;

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(settings.SharePath, fileName);
        await CopyToShareAsync(sourceFilePath, destPath, settings);
    }

    public async Task CopyToShareAsync(string sourceFilePath, string destFilePath, SambaSettings settings)
    {
        if (!settings.Enabled || string.IsNullOrEmpty(settings.SharePath))
        {
            return;
        }

        var destPath = destFilePath;

        _logger.LogInformation("Connecting to Samba share '{SharePath}'", settings.SharePath);
        await ConnectAsync(settings);

        try
        {
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                _logger.LogInformation("Creating directory '{DestDir}' on share", destDir);
                Directory.CreateDirectory(destDir);
            }

            _logger.LogInformation("Copying '{SourceFile}' to Samba share", sourceFilePath);
            File.Copy(sourceFilePath, destPath, overwrite: true);

            if (!File.Exists(destPath))
            {
                throw new IOException($"File copy to Samba share failed: {destPath}");
            }

            var sourceSize = new FileInfo(sourceFilePath).Length;
            var destSize = new FileInfo(destPath).Length;

            if (sourceSize != destSize)
            {
                throw new IOException($"File size mismatch: source={sourceSize}, dest={destSize}");
            }

            _logger.LogInformation("File copied successfully to '{DestPath}' ({Size} bytes)", destPath, destSize);

            if (settings.CreateOkFile)
            {
                var okFilePath = destPath + ".ok";
                _logger.LogInformation("Creating .ok marker file '{OkFile}'", okFilePath);
                File.WriteAllText(okFilePath, $"Backup completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }

            if (settings.DeleteSourceAfterCopy)
            {
                _logger.LogInformation("Deleting source file '{SourceFile}'", sourceFilePath);
                File.Delete(sourceFilePath);

                if (settings.CreateOkFile)
                {
                    var localOkFile = sourceFilePath + ".ok";
                    _logger.LogInformation("Creating local .ok marker file '{OkFile}'", localOkFile);
                    File.WriteAllText(localOkFile, $"Moved to Samba: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
            }
        }
        finally
        {
            _logger.LogInformation("Disconnecting from Samba share");
            await DisconnectAsync(settings);
        }
    }

    private async Task ConnectAsync(SambaSettings settings)
    {
        if (string.IsNullOrEmpty(settings.Username))
        {
            _logger.LogDebug("No credentials provided, assuming pre-authenticated access");
            return;
        }

        var args = $"use \"{settings.SharePath}\"";

        if (!string.IsNullOrEmpty(settings.Username))
        {
            args += $" /user:{settings.Username}";
        }

        if (!string.IsNullOrEmpty(settings.Password))
        {
            args += $" {settings.Password}";
        }

        if (!string.IsNullOrEmpty(settings.Domain))
        {
            args += $" /domain:{settings.Domain}";
        }

        var exitCode = await RunNetCommandAsync(args);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to connect to Samba share (net use exit code: {exitCode})");
        }
    }

    private async Task DisconnectAsync(SambaSettings settings)
    {
        var args = $"use \"{settings.SharePath}\" /delete";
        await RunNetCommandAsync(args);
    }

    private static async Task<int> RunNetCommandAsync(string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "net",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
