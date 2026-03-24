using System.Diagnostics;
using System.Text;

namespace Steak.Host.Configuration;

internal interface ILocalFolderPicker
{
    Task<string?> PickFolderAsync(string? initialPath = null, CancellationToken cancellationToken = default);
}

internal sealed class LocalFolderPicker(ILogger<LocalFolderPicker> logger) : ILocalFolderPicker
{
    public async Task<string?> PickFolderAsync(string? initialPath = null, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return await RunProcessAsync(
                "powershell.exe",
                $"-NoProfile -Sta -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildWindowsScript(initialPath)))}",
                cancellationToken).ConfigureAwait(false);
        }

        if (OperatingSystem.IsMacOS())
        {
            return await RunProcessAsync(
                "osascript",
                $"-e {QuoteArgument("POSIX path of (choose folder with prompt \"Select a folder for Steak\")")}",
                cancellationToken).ConfigureAwait(false);
        }

        if (TryFindExecutable("zenity", out var zenityPath))
        {
            var args = new List<string>
            {
                "--file-selection",
                "--directory",
                "--title=Select a folder for Steak"
            };

            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            {
                args.Add($"--filename={initialPath}{Path.DirectorySeparatorChar}");
            }

            return await RunProcessAsync(zenityPath, string.Join(' ', args.Select(QuoteArgument)), cancellationToken).ConfigureAwait(false);
        }

        if (TryFindExecutable("kdialog", out var kdialogPath))
        {
            var initialDirectory = !string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath)
                ? initialPath
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return await RunProcessAsync(
                kdialogPath,
                $"--getexistingdirectory {QuoteArgument(initialDirectory)}",
                cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Folder browsing is not available on this platform. Enter the path manually.");
    }

    private async Task<string?> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
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

        logger.LogDebug("Opening native folder picker via {FileName} {Arguments}", fileName, arguments);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var output = (await outputTask.ConfigureAwait(false)).Trim();
        var error = (await errorTask.ConfigureAwait(false)).Trim();

        if (process.ExitCode == 0)
        {
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }

        if (string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(error))
        {
            logger.LogDebug("Native folder picker exited without a selection");
            return null;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(error)
                ? $"Native folder picker failed with exit code {process.ExitCode}."
                : $"Native folder picker failed: {error}");
    }

    private static string BuildWindowsScript(string? initialPath)
    {
        var escapedPath = string.IsNullOrWhiteSpace(initialPath)
            ? string.Empty
            : initialPath.Replace("'", "''", StringComparison.Ordinal);

        return $$"""
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
$dialog.Description = 'Select a folder for Steak'
$dialog.ShowNewFolderButton = $true
if ('{{escapedPath}}' -and [System.IO.Directory]::Exists('{{escapedPath}}')) {
    $dialog.SelectedPath = '{{escapedPath}}'
}
if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    [Console]::Write($dialog.SelectedPath)
}
""";
    }

    private static bool TryFindExecutable(string name, out string path)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var candidateDirectory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(candidateDirectory, name);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
