using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Runtime.Versioning;

namespace Steak.Host.Configuration;

internal interface ILocalFolderPicker
{
    Task<string?> PickFolderAsync(string? initialPath = null, CancellationToken cancellationToken = default);
}

internal sealed class LocalFolderPicker(ILogger<LocalFolderPicker> logger) : ILocalFolderPicker
{
    internal static IReadOnlyList<string> WindowsFolderDialogTypeNames { get; } =
    [
        "Microsoft.Win32.OpenFolderDialog, PresentationFramework",
        "System.Windows.Forms.FolderBrowserDialog, System.Windows.Forms"
    ];

    public async Task<string?> PickFolderAsync(string? initialPath = null, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return await RunWindowsFolderPickerAsync(initialPath, cancellationToken).ConfigureAwait(false);
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

    [SupportedOSPlatform("windows")]
    private async Task<string?> RunWindowsFolderPickerAsync(string? initialPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    completion.TrySetResult(TryPickWindowsFolder(initialPath));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            })
            {
                IsBackground = true,
                Name = "Steak Folder Picker"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            using var cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return await completion.Task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "In-process Windows folder picker failed; falling back to Shell.Application BrowseForFolder");
            return await RunWindowsShellBrowseForFolderAsync(cancellationToken).ConfigureAwait(false);
        }
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

    [SupportedOSPlatform("windows")]
    private string? TryPickWindowsFolder(string? initialPath)
    {
        if (TryPickWithOpenFolderDialog(initialPath, out var selectedPath))
        {
            return selectedPath;
        }

        return PickWithFolderBrowserDialog(initialPath);
    }

    private async Task<string?> RunWindowsShellBrowseForFolderAsync(CancellationToken cancellationToken)
    {
        var selectionPath = Path.Combine(Path.GetTempPath(), $"steak-folder-picker-{Guid.NewGuid():N}.txt");
        var errorPath = Path.Combine(Path.GetTempPath(), $"steak-folder-picker-{Guid.NewGuid():N}.err.txt");
        using var process = new Process
        {
            StartInfo = CreateWindowsShellBrowseProcessStartInfo(selectionPath, errorPath)
        };

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        try
        {
            logger.LogDebug("Opening Windows Shell BrowseForFolder picker");
            process.Start();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var selectedPath = await ReadTempFileAsync(selectionPath, cancellationToken).ConfigureAwait(false);
            var error = await ReadTempFileAsync(errorPath, cancellationToken).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                return string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath;
            }

            if (string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException($"Shell folder picker failed with exit code {process.ExitCode}.");
            }

            throw new InvalidOperationException($"Shell folder picker failed: {error}");
        }
        finally
        {
            TryDelete(selectionPath);
            TryDelete(errorPath);
        }
    }

    internal static Type? ResolveWindowsFolderDialogType()
        => WindowsFolderDialogTypeNames
            .Select(ResolveType)
            .FirstOrDefault(type => type is not null);

    internal static ProcessStartInfo CreateWindowsShellBrowseProcessStartInfo(string selectionPath, string errorPath)
    {
        var script = BuildWindowsShellBrowseScript(selectionPath, errorPath);
        return new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Sta -WindowStyle Hidden -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}",
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };
    }

    internal static string BuildWindowsShellBrowseScript(string selectionPath, string errorPath)
    {
        var escapedSelectionPath = selectionPath.Replace("'", "''", StringComparison.Ordinal);
        var escapedErrorPath = errorPath.Replace("'", "''", StringComparison.Ordinal);

        return $$"""
$ErrorActionPreference = 'Stop'
$selectionPath = '{{escapedSelectionPath}}'
$errorPath = '{{escapedErrorPath}}'
try {
    $shell = New-Object -ComObject Shell.Application
    $folder = $shell.BrowseForFolder(0, 'Select a folder for Steak', 0x41, 0)
    if ($null -ne $folder -and $null -ne $folder.Self) {
        $path = $folder.Self.Path
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            [System.IO.File]::WriteAllText($selectionPath, $path, [System.Text.Encoding]::UTF8)
        }
    }
}
catch {
    [System.IO.File]::WriteAllText($errorPath, $_.Exception.Message, [System.Text.Encoding]::UTF8)
    exit 1
}
""";
    }

    [SupportedOSPlatform("windows")]
    private bool TryPickWithOpenFolderDialog(string? initialPath, out string? selectedPath)
    {
        selectedPath = null;
        var dialogType = ResolveType(WindowsFolderDialogTypeNames[0]);
        if (dialogType is null)
        {
            logger.LogDebug("Windows OpenFolderDialog type is unavailable; falling back to FolderBrowserDialog");
            return false;
        }

        try
        {
            var dialog = Activator.CreateInstance(dialogType)
                ?? throw new InvalidOperationException("OpenFolderDialog could not be created.");

            using var disposable = dialog as IDisposable;
            SetProperty(dialogType, dialog, "Title", "Select a folder for Steak");

            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            {
                SetProperty(dialogType, dialog, "InitialDirectory", initialPath);
                SetProperty(dialogType, dialog, "FolderName", initialPath);
            }

            var result = dialogType.GetMethod("ShowDialog", Type.EmptyTypes)?.Invoke(dialog, null);
            if (result is bool confirmed && confirmed)
            {
                selectedPath = dialogType.GetProperty("FolderName")?.GetValue(dialog) as string;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Windows OpenFolderDialog failed; falling back to FolderBrowserDialog");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private string? PickWithFolderBrowserDialog(string? initialPath)
    {
        var dialogType = ResolveType(WindowsFolderDialogTypeNames[1]);
        if (dialogType is null)
        {
            throw new InvalidOperationException("Windows folder browsing is unavailable because no supported dialog type could be loaded.");
        }

        var dialog = Activator.CreateInstance(dialogType)
            ?? throw new InvalidOperationException("FolderBrowserDialog could not be created.");

        using var disposable = dialog as IDisposable;
        SetProperty(dialogType, dialog, "Description", "Select a folder for Steak");
        SetProperty(dialogType, dialog, "ShowNewFolderButton", true);

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            SetProperty(dialogType, dialog, "SelectedPath", initialPath);
        }

        var owner = CreateTopMostFolderBrowserOwner(dialogType.Assembly);
        try
        {
            var result = dialogType.GetMethod("ShowDialog", [owner.GetType().GetInterface("IWin32Window")!])?.Invoke(dialog, [owner]);
            if (string.Equals(result?.ToString(), "OK", StringComparison.Ordinal))
            {
                return dialogType.GetProperty("SelectedPath")?.GetValue(dialog) as string;
            }

            return null;
        }
        finally
        {
            CloseOwnerWindow(owner);
        }
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

    private static async Task<string?> ReadTempFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return content.Trim();
    }

    private static void SetProperty(Type type, object instance, string propertyName, object? value)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite == true)
        {
            property.SetValue(instance, value);
        }
    }

    [SupportedOSPlatform("windows")]
    private static object CreateTopMostFolderBrowserOwner(Assembly windowsFormsAssembly)
    {
        var formType = windowsFormsAssembly.GetType("System.Windows.Forms.Form")
            ?? throw new InvalidOperationException("System.Windows.Forms.Form type could not be loaded.");
        var form = Activator.CreateInstance(formType)
            ?? throw new InvalidOperationException("Folder picker owner window could not be created.");

        SetProperty(formType, form, "ShowInTaskbar", false);
        SetProperty(formType, form, "TopMost", true);
        SetProperty(formType, form, "Opacity", 0d);

        var fixedToolWindow = Enum.Parse(
            windowsFormsAssembly.GetType("System.Windows.Forms.FormBorderStyle")
                ?? throw new InvalidOperationException("FormBorderStyle enum could not be loaded."),
            "FixedToolWindow",
            ignoreCase: false);
        SetProperty(formType, form, "FormBorderStyle", fixedToolWindow);

        var manualStartPosition = Enum.Parse(
            windowsFormsAssembly.GetType("System.Windows.Forms.FormStartPosition")
                ?? throw new InvalidOperationException("FormStartPosition enum could not be loaded."),
            "Manual",
            ignoreCase: false);
        SetProperty(formType, form, "StartPosition", manualStartPosition);

        var drawingPrimitives = Assembly.Load("System.Drawing.Primitives");
        var pointType = drawingPrimitives.GetType("System.Drawing.Point")
            ?? throw new InvalidOperationException("System.Drawing.Point type could not be loaded.");
        var sizeType = drawingPrimitives.GetType("System.Drawing.Size")
            ?? throw new InvalidOperationException("System.Drawing.Size type could not be loaded.");
        var location = Activator.CreateInstance(pointType, -32000, -32000)
            ?? throw new InvalidOperationException("Owner location could not be created.");
        var size = Activator.CreateInstance(sizeType, 1, 1)
            ?? throw new InvalidOperationException("Owner size could not be created.");

        SetProperty(formType, form, "Location", location);
        SetProperty(formType, form, "Size", size);

        formType.GetMethod("Show", Type.EmptyTypes)?.Invoke(form, null);
        formType.GetMethod("Activate", Type.EmptyTypes)?.Invoke(form, null);
        return form;
    }

    private static void CloseOwnerWindow(object owner)
    {
        var ownerType = owner.GetType();
        try
        {
            ownerType.GetMethod("Close", Type.EmptyTypes)?.Invoke(owner, null);
        }
        finally
        {
            (owner as IDisposable)?.Dispose();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static Type? ResolveType(string assemblyQualifiedTypeName)
    {
        var directType = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
        if (directType is not null)
        {
            return directType;
        }

        var separatorIndex = assemblyQualifiedTypeName.IndexOf(',');
        if (separatorIndex < 0)
        {
            return null;
        }

        var typeName = assemblyQualifiedTypeName[..separatorIndex].Trim();
        var assemblyName = assemblyQualifiedTypeName[(separatorIndex + 1)..].Trim();

        try
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            return assembly.GetType(typeName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }
}
