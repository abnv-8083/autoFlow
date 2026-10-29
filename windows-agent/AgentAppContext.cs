using System.Windows.Forms;

namespace WindowsAgent;

/// <summary>
/// ApplicationContext for Remote Laptop Control.
/// Decouples the application lifecycle from the main form handle,
/// ensuring that minimizing/hiding the UI to the Windows System Tray
/// never terminates the application message loop.
/// </summary>
public class AgentAppContext : ApplicationContext
{
    private readonly MainForm _mainForm;
    private readonly AgentClient _agent;

    public AgentAppContext(AgentClient? agent = null, bool startMinimized = false)
    {
        _agent = agent ?? new AgentClient();
        _mainForm = new MainForm(_agent, startMinimized, onExitRequested: ExitThread);

        if (!startMinimized)
        {
            _mainForm.Show();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mainForm.Dispose();
        }
        base.Dispose(disposing);
    }
}
