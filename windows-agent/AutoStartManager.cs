using Microsoft.Win32;

namespace WindowsAgent;

/// <summary>
/// Manages Windows startup registry settings for automatic launch at user login.
/// </summary>
public static class AutoStartManager
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "RemoteLaptopAgent";

    /// <summary>
    /// Checks whether the application is currently registered to start with Windows.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            var value = key?.GetValue(AppName);
            return value != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to check startup registry key: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Registers the application in HKCU Run key to automatically start on user logon.
    /// </summary>
    /// <param name="customPath">Optional custom path; defaults to the current executable.</param>
    /// <param name="arguments">Optional arguments such as --tray.</param>
    public static bool Enable(string? customPath = null, string arguments = "--tray")
    {
        try
        {
            string exePath = customPath ?? Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                Console.WriteLine("[Error] Cannot determine current executable path.");
                return false;
            }

            // Wrap path in quotes to handle directory names with spaces
            string registryValue = string.IsNullOrWhiteSpace(arguments)
                ? $"\"{exePath}\""
                : $"\"{exePath}\" {arguments.Trim()}";

            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null)
            {
                Console.WriteLine("[Error] Unable to open HKCU Run registry key.");
                return false;
            }

            key.SetValue(AppName, registryValue);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to enable auto-start: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes the application from the HKCU Run key.
    /// </summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return false;

            if (key.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to disable auto-start: {ex.Message}");
            return false;
        }
    }
}
