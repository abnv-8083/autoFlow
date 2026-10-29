using System.Runtime.InteropServices;
using WindowsAgent;

// Win32 API imports to hide/show the console window dynamically
[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();

[DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

const int SW_HIDE = 0;

// Parse optional --server <url> parameter
string? serverArg = null;
for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--server" || args[i] == "-s") && i + 1 < args.Length)
    {
        serverArg = args[i + 1];
        AgentClient.SaveServerUrl(serverArg);
        break;
    }
}

// If invoked with --tray, start in Windows System Tray mode
if (args.Any(a => a == "--tray"))
{
    IntPtr handle = GetConsoleWindow();
    if (handle != IntPtr.Zero)
    {
        ShowWindow(handle, SW_HIDE);
    }
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new TrayApplicationContext(new AgentClient(serverUrl: serverArg)));
    return;
}

// If invoked with --pair, start in pairing mode
if (args.Any(a => a == "--pair"))
{
    await RunPairingAgentAsync();
    return;
}

// If invoked with --background, --silent, or --connect, run in live agent service mode
if (args.Any(a => a == "--background" || a == "--silent" || a == "--connect"))
{
    if (args.Any(a => a == "--background" || a == "--silent"))
    {
        IntPtr handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SW_HIDE);
        }
    }

    await RunLiveAgentAsync();
    return;
}

// Otherwise, run the interactive test console
await RunInteractiveMenuAsync();

static async Task RunInteractiveMenuAsync()
{
    Console.WriteLine("==================================================");
    Console.WriteLine("      Remote Laptop Control - Windows Agent       ");
    Console.WriteLine("              Local Test & Config Mode            ");
    Console.WriteLine("==================================================");

    bool running = true;

    while (running)
    {
        bool autoStartEnabled = AutoStartManager.IsEnabled();

        Console.WriteLine();
        Console.WriteLine("System Power Operations:");
        Console.WriteLine("  [1] Lock Workstation");
        Console.WriteLine("  [2] Sleep Laptop");
        Console.WriteLine("  [3] Restart Laptop (with confirmation)");
        Console.WriteLine("  [4] Shutdown Laptop (with confirmation)");
        Console.WriteLine("  [5] Abort Pending Shutdown");
        Console.WriteLine();
        Console.WriteLine("Background & Auto-Start Configuration:");
        Console.WriteLine($"  [6] Auto-Start with Windows : [{(autoStartEnabled ? "ENABLED" : "DISABLED")}]");
        Console.WriteLine("  [7] Toggle Auto-Start Setting");
        Console.WriteLine("  [8] Test Background Execution Loop (5s)");
        Console.WriteLine();
        Console.WriteLine("Live WebSocket Client & Pairing:");
        Console.WriteLine("  [9] Connect to WebSocket Relay Server (Live Mode)");
        Console.WriteLine("  [t] Launch Windows System Tray App (Minimize to Tray)");
        Console.WriteLine("  [p] Generate 6-Digit PIN Pairing Code for Mobile App");
        Console.WriteLine("  [0] Exit");
        Console.Write("\nEnter choice (0-9, t, p): ");

        string? input = Console.ReadLine()?.Trim();

        switch (input)
        {
            case "1":
                Console.WriteLine("\n[Lock] Executing LockWorkStation()...");
                bool locked = SystemOperations.Lock();
                Console.WriteLine(locked ? "[Success] Workstation locked." : "[Failed] Could not lock workstation.");
                break;

            case "2":
                Console.WriteLine("\n[Sleep] Preparing to sleep in 3 seconds (press Ctrl+C to abort)...");
                for (int i = 3; i > 0; i--)
                {
                    Console.WriteLine($"Sleeping in {i}...");
                    Thread.Sleep(1000);
                }
                Console.WriteLine("Executing SetSuspendState()...");
                bool sleeping = SystemOperations.Sleep();
                Console.WriteLine(sleeping ? "[Success] Sleep signal sent." : "[Failed] Could not enter sleep mode.");
                break;

            case "3":
                Console.Write("\n[Restart] WARNING: This will reboot your laptop. Are you sure? (y/N): ");
                string? confirmRestart = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (confirmRestart == "y" || confirmRestart == "yes")
                {
                    Console.WriteLine("Scheduling restart with 10-second warning (run option 5 to abort)...");
                    bool restarted = SystemOperations.Restart(timeoutSeconds: 10);
                    Console.WriteLine(restarted ? "[Success] Restart scheduled in 10 seconds." : "[Failed] Could not schedule restart.");
                }
                else
                {
                    Console.WriteLine("Restart canceled.");
                }
                break;

            case "4":
                Console.Write("\n[Shutdown] WARNING: This will power off your laptop. Are you sure? (y/N): ");
                string? confirmShutdown = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (confirmShutdown == "y" || confirmShutdown == "yes")
                {
                    Console.WriteLine("Scheduling shutdown with 10-second warning (run option 5 to abort)...");
                    bool shutdown = SystemOperations.Shutdown(timeoutSeconds: 10);
                    Console.WriteLine(shutdown ? "[Success] Shutdown scheduled in 10 seconds." : "[Failed] Could not schedule shutdown.");
                }
                else
                {
                    Console.WriteLine("Shutdown canceled.");
                }
                break;

            case "5":
                Console.WriteLine("\n[Abort] Canceling pending restart/shutdown...");
                bool aborted = SystemOperations.AbortShutdown();
                Console.WriteLine(aborted ? "[Success] Pending shutdown/restart aborted." : "[Info] No pending shutdown to abort, or failed.");
                break;

            case "6":
                Console.WriteLine($"\n[Status] Auto-Start is currently: {(autoStartEnabled ? "ENABLED in HKCU Run key" : "DISABLED")}");
                break;

            case "7":
                if (autoStartEnabled)
                {
                    bool ok = AutoStartManager.Disable();
                    Console.WriteLine(ok ? "\n[Success] Auto-Start disabled (removed from registry)." : "\n[Failed] Could not disable auto-start.");
                }
                else
                {
                    bool ok = AutoStartManager.Enable();
                    Console.WriteLine(ok ? "\n[Success] Auto-Start enabled (added to HKCU Run key)." : "\n[Failed] Could not enable auto-start.");
                }
                break;

            case "8":
                Console.WriteLine("\n[Background Loop Test]");
                Console.WriteLine("Simulating background loop for 5 seconds using CancellationToken...");
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    int ticks = 0;
                    while (!cts.Token.IsCancellationRequested)
                    {
                        ticks++;
                        Console.WriteLine($"  [Heartbeat] Tick #{ticks} - Memory: {GC.GetTotalMemory(false) / 1024} KB");
                        try
                        {
                            Task.Delay(1000, cts.Token).Wait();
                        }
                        catch (AggregateException) when (cts.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                }
                Console.WriteLine("[Success] Background loop finished cleanly.");
                break;

            case "9":
                Console.WriteLine("\nStarting Live WebSocket Client Mode...");
                Console.WriteLine("Press Ctrl+C to return to menu.\n");
                await RunLiveAgentAsync();
                break;

            case "t":
            case "T":
                Console.WriteLine("\nMinimizing to Windows System Tray...");
                IntPtr h = GetConsoleWindow();
                if (h != IntPtr.Zero) ShowWindow(h, SW_HIDE);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext());
                break;

            case "p":
            case "P":
                Console.WriteLine("\nStarting Pairing Mode...");
                Console.WriteLine("Press Ctrl+C to return to menu.\n");
                await RunPairingAgentAsync();
                break;

            case "0":
                Console.WriteLine("\nExiting. Goodbye!");
                running = false;
                break;

            default:
                Console.WriteLine("\nInvalid option. Please choose 0 to 9, or 'p'.");
                break;
        }
    }
}

static async Task RunLiveAgentAsync()
{
    using var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var agent = new AgentClient();
    agent.AuthenticationFailed += msg =>
    {
        Console.WriteLine("\n[Pairing Tip] This laptop is not yet paired with your mobile app.");
        Console.WriteLine("[Pairing Tip] Run 'RemoteLaptopAgent.exe --pair' to generate a 6-digit PIN and pair your phone.\n");
    };

    await agent.StartAsync(cts.Token);
}

static async Task RunPairingAgentAsync()
{
    using var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var agent = new AgentClient();
    await agent.StartPairingAsync(cts.Token);
}
