using Steak.Host.Configuration;

namespace Steak.Tests.Host;

public sealed class LocalFolderPickerTests
{
    [Fact]
    public void WindowsFolderDialogTypeNames_PreferModernOpenFolderDialog()
    {
        Assert.Collection(
            LocalFolderPicker.WindowsFolderDialogTypeNames,
            typeName => Assert.Equal("Microsoft.Win32.OpenFolderDialog, PresentationFramework", typeName),
            typeName => Assert.Equal("System.Windows.Forms.FolderBrowserDialog, System.Windows.Forms", typeName));
    }

    [Fact]
    public void ResolveWindowsFolderDialogType_ReturnsKnownDialogTypeWhenAvailable()
    {
        var exception = Record.Exception(LocalFolderPicker.ResolveWindowsFolderDialogType);
        Assert.Null(exception);

        var type = LocalFolderPicker.ResolveWindowsFolderDialogType();

        if (type is not null)
        {
            Assert.Contains(type.FullName, new[] { "Microsoft.Win32.OpenFolderDialog", "System.Windows.Forms.FolderBrowserDialog" });
        }
    }

    [Fact]
    public void BuildWindowsShellBrowseScript_UsesShellBrowseForFolderFlagsAndWritesSelectionFile()
    {
        var script = LocalFolderPicker.BuildWindowsShellBrowseScript("D:\\Temp\\selection.txt", "D:\\Temp\\error.txt");

        Assert.Contains("BrowseForFolder", script, StringComparison.Ordinal);
        Assert.Contains("0x41", script, StringComparison.Ordinal);
        Assert.Contains("D:\\Temp\\selection.txt", script, StringComparison.Ordinal);
        Assert.Contains("D:\\Temp\\error.txt", script, StringComparison.Ordinal);
        Assert.Contains("WriteAllText($selectionPath", script, StringComparison.Ordinal);
    }
}
