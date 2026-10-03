using Microsoft.Win32;

namespace DynamicIsland.Core;

/// <summary>"Start with Windows" via the per-user Run key.</summary>
public static class Startup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DynamicIsland";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;
            if (value) key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
