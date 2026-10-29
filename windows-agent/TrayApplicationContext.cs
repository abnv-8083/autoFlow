using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WindowsAgent;

/// <summary>
/// Provides a native Windows System Tray icon, notification balloon tips,
/// context menu, and background lifecycle for the Remote Laptop agent.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AgentClient _agent;
    private readonly CancellationTokenSource _cts = new();
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _autoStartMenuItem;
    private readonly SynchronizationContext _syncContext;

    public TrayApplicationContext(AgentClient? agent = null)
    {
        _agent = agent ?? new AgentClient();
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        // 1. Initialize NotifyIcon early
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(online: false),
            Text = "Remote Laptop Control (Connecting...)",
            Visible = true
        };

        // 2. Initialize context menu
        var contextMenu = new ContextMenuStrip();

        // Header: Remote Laptop Control (MachineName)
        var titleItem = new ToolStripMenuItem($"Remote Laptop Control ({_agent.DeviceId})")
        {
            Enabled = false,
            Font = new Font(Control.DefaultFont, FontStyle.Bold)
        };
        contextMenu.Items.Add(titleItem);

        // Status Item
        _statusMenuItem = new ToolStripMenuItem("Status: Connecting to relay...")
        {
            Enabled = false
        };
        contextMenu.Items.Add(_statusMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Dynamic Pairing Item
        var pairItem = new ToolStripMenuItem("Pair New Phone...", null, (_, _) =>
        {
            TriggerPairing();
        });
        contextMenu.Items.Add(pairItem);

        // Start with Windows Toggle
        _autoStartMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = AutoStartManager.IsEnabled(),
            CheckOnClick = true
        };
        _autoStartMenuItem.CheckedChanged += (_, _) =>
        {
            if (_autoStartMenuItem.Checked)
            {
                AutoStartManager.Enable(arguments: "--tray");
                _notifyIcon.ShowBalloonTip(3000, "Auto-Start Enabled", "Agent will automatically start in system tray at Windows login.", ToolTipIcon.Info);
            }
            else
            {
                AutoStartManager.Disable();
                _notifyIcon.ShowBalloonTip(3000, "Auto-Start Disabled", "Agent removed from Windows startup.", ToolTipIcon.Info);
            }
        };
        contextMenu.Items.Add(_autoStartMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Relay Server URL Info (Fixed permanent default)
        var serverInfoItem = new ToolStripMenuItem($"Relay: {_agent.ServerUrl}")
        {
            Enabled = false
        };
        contextMenu.Items.Add(serverInfoItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Quick Command: Lock Workstation
        var lockItem = new ToolStripMenuItem("Lock Workstation Now", null, (_, _) =>
        {
            SystemOperations.Lock();
        });
        contextMenu.Items.Add(lockItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Exit Agent
        var exitItem = new ToolStripMenuItem("Exit Agent", null, (_, _) =>
        {
            Exit();
        });
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        // Double-click tray icon to show status
        _notifyIcon.DoubleClick += (_, _) =>
        {
            _notifyIcon.ShowBalloonTip(
                3000,
                "Remote Laptop Control",
                _agent.IsConnected ? $"Online & listening for phone commands on {_agent.ServerUrl}" : "Connecting to relay server...",
                _agent.IsConnected ? ToolTipIcon.Info : ToolTipIcon.Warning
            );
        };

        // Balloon tip click triggers pairing if offline/unpaired
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            if (!_agent.IsConnected)
            {
                TriggerPairing();
            }
        };

        // 3. Wire up Agent events
        _agent.AuthenticationFailed += msg =>
        {
            _syncContext.Post(_ =>
            {
                _notifyIcon.Icon = CreateTrayIcon(false);
                _notifyIcon.Text = $"Remote Laptop Control - Unpaired ({_agent.DeviceId})";
                _statusMenuItem.Text = "Status: Unpaired (Click to Pair New Phone...)";

                _notifyIcon.ShowBalloonTip(
                    7000,
                    "Remote Laptop - Pairing Needed",
                    "This laptop needs to be paired with your phone. Click here to generate a 6-digit PIN.",
                    ToolTipIcon.Warning
                );
            }, null);
        };

        _agent.ConnectionStatusChanged += isOnline =>
        {
            _syncContext.Post(_ =>
            {
                _notifyIcon.Icon = CreateTrayIcon(isOnline);
                _notifyIcon.Text = isOnline 
                    ? $"Remote Laptop Control - Online ({_agent.DeviceId})" 
                    : "Remote Laptop Control - Offline (Reconnecting...)";

                _statusMenuItem.Text = isOnline 
                    ? "Status: Online (Relay Connected)" 
                    : "Status: Offline (Reconnecting...)";

                if (isOnline)
                {
                    _notifyIcon.ShowBalloonTip(
                        3000,
                        "Remote Laptop Online",
                        $"Connected to relay as '{_agent.DeviceId}'. Ready for remote commands.",
                        ToolTipIcon.Info
                    );
                }
            }, null);
        };

        _agent.CommandExecuted += (cmd, success) =>
        {
            _syncContext.Post(_ =>
            {
                _notifyIcon.ShowBalloonTip(
                    4000,
                    "Remote Command Executed",
                    $"Phone triggered '{cmd}' (Success: {success})",
                    success ? ToolTipIcon.Info : ToolTipIcon.Warning
                );
            }, null);
        };

        // 4. Start agent background loop
        Task.Run(async () =>
        {
            await _agent.StartAsync(_cts.Token);
        });
    }

    private void TriggerPairing()
    {
        Task.Run(async () =>
        {
            try
            {
                using var pairCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var pairingAgent = new AgentClient();

                pairingAgent.PairingCodeReceived += (code, expiresIn) =>
                {
                    _syncContext.Post(_ =>
                    {
                        _notifyIcon.ShowBalloonTip(
                            7000,
                            "Remote Laptop - Pairing Code",
                            $"PIN: {code}\nEnter this code on your phone app to pair (valid for {expiresIn / 60}m).",
                            ToolTipIcon.Info
                        );
                        ShowPairingCodeWindow(code, expiresIn);
                    }, null);
                };

                pairingAgent.PairingCompleted += phoneId =>
                {
                    _syncContext.Post(_ =>
                    {
                        _notifyIcon.ShowBalloonTip(
                            5000,
                            "Pairing Succeeded!",
                            $"Linked successfully with {phoneId}. Laptop is now ready.",
                            ToolTipIcon.Info
                        );
                        _statusMenuItem.Text = "Status: Online (Relay Connected)";
                        _notifyIcon.Icon = CreateTrayIcon(true);
                    }, null);
                };

                await pairingAgent.StartPairingAsync(pairCts.Token);
            }
            catch (Exception ex)
            {
                _syncContext.Post(_ =>
                {
                    _notifyIcon.ShowBalloonTip(
                        4000,
                        "Pairing Error",
                        $"Could not connect to relay: {ex.Message}",
                        ToolTipIcon.Error
                    );
                }, null);
            }
        });
    }

    private void Exit()
    {
        _cts.Cancel();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Generates a crisp 32x32 icon programmatically.
    /// Green circular badge when connected, sleek slate-gray when disconnected.
    /// </summary>
    private static Icon CreateTrayIcon(bool online)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Outer circle
            var color = online ? Color.FromArgb(16, 185, 129) : Color.FromArgb(107, 114, 128);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);

            // Laptop icon drawing in white
            using var pen = new Pen(Color.White, 2f);
            // Screen outline
            g.DrawRectangle(pen, 9, 8, 14, 10);
            // Keyboard base
            g.DrawLine(pen, 7, 21, 25, 21);
        }

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private static void ShowPairingCodeWindow(string code, int expiresInSeconds)
    {
        var form = new Form
        {
            Text = "Remote Laptop - Device Pairing",
            Width = 380,
            Height = 240,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true,
            BackColor = Color.FromArgb(18, 20, 24)
        };

        var labelHeader = new Label
        {
            Text = "Enter this 6-Digit PIN on your phone:",
            ForeColor = Color.FromArgb(156, 163, 175),
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var labelPin = new Label
        {
            Text = code,
            ForeColor = Color.FromArgb(59, 130, 246),
            Font = new Font("Segoe UI", 32, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 65,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var labelExpires = new Label
        {
            Text = $"Code expires in {expiresInSeconds / 60} minutes.",
            ForeColor = Color.FromArgb(107, 114, 128),
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var btnClose = new Button
        {
            Text = "Dismiss",
            Dock = DockStyle.Bottom,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(38, 44, 56)
        };
        btnClose.Click += (_, _) => form.Close();

        form.Controls.Add(btnClose);
        form.Controls.Add(labelExpires);
        form.Controls.Add(labelPin);
        form.Controls.Add(labelHeader);

        form.Show();
    }
}
