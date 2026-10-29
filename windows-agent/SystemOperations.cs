using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsAgent;

/// <summary>
/// Provides native Windows system operations using Win32 APIs and system binaries.
/// </summary>
public static class SystemOperations
{
    // Native Win32 API from user32.dll for locking the workstation
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    // Native Windows Power Management API from powrprof.dll for sleep/standby
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool bHibernate,
        [MarshalAs(UnmanagedType.Bool)] bool bForce,
        [MarshalAs(UnmanagedType.Bool)] bool bWakeupEventsDisabled);

    /// <summary>
    /// Locks the active Windows workstation session (equivalent to Win + L).
    /// </summary>
    public static bool Lock()
    {
        bool result = LockWorkStation();
        if (!result)
        {
            int errorCode = Marshal.GetLastWin32Error();
            Console.WriteLine($"[Error] LockWorkStation failed with Win32 error code: {errorCode}");
        }
        return result;
    }

    /// <summary>
    /// Puts the laptop into sleep (standby) mode.
    /// </summary>
    public static bool Sleep()
    {
        // hibernate = false: Standby / Sleep
        // forceCritical = false: allow applications to cleanly suspend
        // disableWakeEvent = false: allow power button, keyboard, or wake timers to wake PC
        bool result = SetSuspendState(false, false, false);
        if (!result)
        {
            int errorCode = Marshal.GetLastWin32Error();
            Console.WriteLine($"[Error] SetSuspendState failed with Win32 error code: {errorCode}");
        }
        return result;
    }

    /// <summary>
    /// Restarts the Windows machine.
    /// Uses the secure system path to shutdown.exe to prevent binary hijacking.
    /// </summary>
    /// <param name="timeoutSeconds">Delay before rebooting (default: 0 for immediate).</param>
    public static bool Restart(int timeoutSeconds = 0)
    {
        return ExecuteShutdownProcess($"/r /t {timeoutSeconds}");
    }

    /// <summary>
    /// Shuts down the Windows machine.
    /// Uses the secure system path to shutdown.exe to prevent binary hijacking.
    /// </summary>
    /// <param name="timeoutSeconds">Delay before shutdown (default: 0 for immediate).</param>
    public static bool Shutdown(int timeoutSeconds = 0)
    {
        return ExecuteShutdownProcess($"/s /t {timeoutSeconds}");
    }

    /// <summary>
    /// Cancels a pending timed restart or shutdown.
    /// </summary>
    public static bool AbortShutdown()
    {
        return ExecuteShutdownProcess("/a");
    }

    /// <summary>
    /// Helper to execute shutdown.exe from the verified Windows System32 directory.
    /// </summary>
    private static bool ExecuteShutdownProcess(string arguments)
    {
        try
        {
            // Always resolve explicitly to C:\Windows\System32\shutdown.exe for security
            string systemDir = Environment.SystemDirectory;
            string shutdownExe = Path.Combine(systemDir, "shutdown.exe");

            var startInfo = new ProcessStartInfo
            {
                FileName = shutdownExe,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine("[Error] Failed to start shutdown process.");
                return false;
            }

            process.WaitForExit(5000);

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                Console.WriteLine($"[Error] shutdown.exe exited with code {process.ExitCode}: {error.Trim()}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Exception] Failed to execute shutdown command: {ex.Message}");
            return false;
        }
    }
}
