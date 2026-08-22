using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class CompressionService
{
    private readonly ILogger<CompressionService> _logger;
    private readonly string _sevenZipPath;

    public CompressionService(ILogger<CompressionService> logger)
    {
        _logger = logger;
        _sevenZipPath = Find7ZipPath();
    }

    public async Task CompressFileAsync(string inputFilePath, CompressionSettings settings)
    {
        if (!settings.Compress || string.IsNullOrEmpty(_sevenZipPath))
        {
            _logger.LogWarning("Compression skipped: 7-Zip not found or compression disabled");
            return;
        }

        var outputFilePath = inputFilePath + ".7z";

        _logger.LogInformation("Compressing file '{InputFile}' to '{OutputFile}'", inputFilePath, outputFilePath);

        var arguments = BuildArguments(inputFilePath, outputFilePath, settings.Password, settings.CompressionLevel);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _sevenZipPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("7-Zip compression failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
            throw new InvalidOperationException($"7-Zip compression failed: {error}");
        }

        _logger.LogInformation("Compression completed successfully");
    }

    private static string BuildArguments(string inputPath, string outputPath, string? password, string compressionLevel)
    {
        var args = $"a -t7z \"{outputPath}\" \"{inputPath}\"";

        args = compressionLevel.ToLower() switch
        {
            "fastest" => args + " -m0=LZMA2 -mx=1",
            "fast" => args + " -m0=LZMA2 -mx=3",
            "normal" => args + " -m0=LZMA2 -mx=5",
            "maximum" => args + " -m0=LZMA2 -mx=7",
            "ultra" => args + " -m0=LZMA2 -mx=9",
            _ => args + " -m0=LZMA2 -mx=5"
        };

        if (!string.IsNullOrEmpty(password))
        {
            args += $" -p\"{password}\" -mhe=on";
        }

        return args;
    }

    private static string Find7ZipPath()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            "7z" // Try PATH
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) || path == "7z")
            {
                return path;
            }
        }

        return string.Empty;
    }
}
