using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MssqlBackup.Console.Models;

namespace MssqlBackup.Console.Services;

public class AgeService
{
    private readonly ILogger<AgeService> _logger;
    private readonly string _agePath;

    public AgeService(ILogger<AgeService> logger)
    {
        _logger = logger;
        _agePath = FindAgePath();
    }

    /// <summary>
    /// Encrypts file with age using recipient key (-r). Returns path to .age file.
    /// Flow: 7zip -> age (skips if not enabled)
    /// </summary>
    public async Task<string> EncryptFileAsync(string inputFilePath, AgeSettings settings)
    {
        if (!settings.Enabled)
            return inputFilePath;

        if (string.IsNullOrWhiteSpace(settings.Recipient) && string.IsNullOrWhiteSpace(settings.RecipientsFile))
            throw new InvalidOperationException("Age encryption enabled but Recipient nor RecipientsFile is configured (AgeSettings:Recipient)");

        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException($"Source file for age encryption not found: {inputFilePath}");

        var ageBinary = ResolveAgeBinary(settings);
        if (string.IsNullOrEmpty(ageBinary))
            throw new InvalidOperationException("Age binary not found (age/age.exe). Install from https://github.com/FiloSottile/age/releases or set AgeSettings:AgePath");

        if (!string.IsNullOrWhiteSpace(settings.RecipientsFile) && !File.Exists(settings.RecipientsFile))
            throw new FileNotFoundException($"Age recipients file not found: {settings.RecipientsFile}");

        var outputFilePath = inputFilePath + ".age";

        _logger.LogInformation("Encrypting file '{InputFile}' to '{OutputFile}' with age (recipient: {Recipient})",
            inputFilePath, outputFilePath,
            string.IsNullOrWhiteSpace(settings.RecipientsFile) ? MaskRecipient(settings.Recipient) : $"file:{settings.RecipientsFile}");

        var arguments = BuildArguments(inputFilePath, outputFilePath, settings);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ageBinary,
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
            _logger.LogError("Age encryption failed with exit code {ExitCode}: {Error} {Output}", process.ExitCode, error, output);
            throw new InvalidOperationException($"Age encryption failed (exit {process.ExitCode}): {error} {output}");
        }

        if (!File.Exists(outputFilePath))
            throw new IOException($"Age encryption failed - output not found: {outputFilePath}");

        var srcSize = new FileInfo(inputFilePath).Length;
        var dstSize = new FileInfo(outputFilePath).Length;
        if (dstSize == 0)
            throw new IOException($"Age encryption produced empty file: {outputFilePath}");

        _logger.LogInformation("Age encryption completed '{OutputFile}' ({Size} bytes, source {SrcSize} bytes)", outputFilePath, dstSize, srcSize);

        return outputFilePath;
    }

    private string ResolveAgeBinary(AgeSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AgePath) && settings.AgePath != "age")
        {
            if (File.Exists(settings.AgePath))
                return settings.AgePath;
            // if explicit path but not found, return it anyway - process will fail with clear error
            if (Path.IsPathRooted(settings.AgePath) || settings.AgePath.Contains(Path.DirectorySeparatorChar))
                return settings.AgePath;
        }
        return _agePath;
    }

    private static string BuildArguments(string inputPath, string outputPath, AgeSettings settings)
    {
        // age --encrypt -r <recipient> -o <output> <input>
        // or age --encrypt --recipients-file <file> -o <output> <input>
        var args = "";

        if (!string.IsNullOrWhiteSpace(settings.RecipientsFile))
        {
            args = $"--encrypt --recipients-file \"{settings.RecipientsFile}\" -o \"{outputPath}\" \"{inputPath}\"";
        }
        else
        {
            var recipients = settings.Recipient
                .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Where(r => !string.IsNullOrEmpty(r))
                .ToArray();

            if (recipients.Length == 0)
                throw new InvalidOperationException("Age Recipient is empty");

            var recipientArgs = string.Join(" ", recipients.Select(r => $"-r \"{r}\""));
            args = $"--encrypt {recipientArgs} -o \"{outputPath}\" \"{inputPath}\"";
        }

        return args;
    }

    private static string MaskRecipient(string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return "(empty)";
        if (recipient.Length <= 12) return recipient;
        return recipient[..8] + "***" + recipient[^4..];
    }

    private static string FindAgePath()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\age\age.exe",
            @"C:\Program Files (x86)\age\age.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "age", "age.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "age", "age.exe"),
            @"C:\tools\age\age.exe",
            "/usr/bin/age",
            "/usr/local/bin/age",
            "age" // try PATH
        };

        foreach (var path in possiblePaths)
        {
            if (path == "age") return path;
            if (File.Exists(path)) return path;
        }

        return "age";
    }
}
