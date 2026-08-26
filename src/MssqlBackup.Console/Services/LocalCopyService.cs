using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class LocalCopyService
{
    private readonly ILogger<LocalCopyService> _logger;

    public LocalCopyService(ILogger<LocalCopyService> logger)
    {
        _logger = logger;
    }

    public async Task CopyAsync(string sourceFilePath, string destFilePath, LocalCopySettings settings)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.DestinationPath))
            return;

        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source file not found: {sourceFilePath}");

        var destDir = Path.GetDirectoryName(destFilePath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            _logger.LogInformation("Creating directory '{DestDir}' for local copy", destDir);
            Directory.CreateDirectory(destDir);
        }

        _logger.LogInformation("Copying '{Source}' to local folder '{Dest}'", sourceFilePath, destFilePath);

        // File.Copy is synchronous; wrap in Task.Run to avoid blocking
        await Task.Run(() => File.Copy(sourceFilePath, destFilePath, overwrite: true));

        if (!File.Exists(destFilePath))
            throw new IOException($"Local copy failed: {destFilePath}");

        var srcSize = new FileInfo(sourceFilePath).Length;
        var dstSize = new FileInfo(destFilePath).Length;
        if (srcSize != dstSize)
            throw new IOException($"File size mismatch after local copy: source={srcSize}, dest={dstSize}");

        _logger.LogInformation("Local copy successful '{Dest}' ({Size} bytes)", destFilePath, dstSize);

        if (settings.DeleteSourceAfterCopy)
        {
            _logger.LogInformation("Deleting source file '{Source}' after local copy", sourceFilePath);
            File.Delete(sourceFilePath);
        }
    }
}
