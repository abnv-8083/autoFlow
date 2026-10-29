using WindowsAgent;

// 1. Parse optional command line arguments
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

// 2. Determine startup state:
// If started with --tray / --startup / --background AND already has a paired token, start directly in tray.
// Otherwise (normal double-click launch or unpaired), open the actual UI window.
bool startMinimized = args.Any(a => a == "--tray" || a == "--silent" || a == "--background" || a == "--startup") 
    && AgentClient.HasPairedToken;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

try
{
    Console.SetOut(TextWriter.Null);
    Console.SetError(TextWriter.Null);
}
catch { }

try
{
    var agent = new AgentClient(serverUrl: serverArg);
    Application.Run(new AgentAppContext(agent, startMinimized: startMinimized));
}
catch (Exception ex)
{
    MessageBox.Show(ex.ToString(), "Remote Laptop Control Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
