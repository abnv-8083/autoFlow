using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WindowsAgent;

/// <summary>
/// Native desktop UI window for Remote Laptop Control.
/// Displays live connection state, 6-digit dynamic pairing PIN,
/// auto-minimizes to the System Tray on first phone connection,
/// and provides tray controls.
/// </summary>
public class MainForm : Form
{
    private readonly AgentClient _agent;
    private readonly NotifyIcon _notifyIcon;
    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronizationContext _syncContext;
    private readonly Action? _onExitRequested;
    private bool _isExiting = false;
    private bool _startMinimized = false;

    // Header Controls
    private Label _lblStatusDot = null!;
    private Label _lblStatusText = null!;
    private Panel _pnlStatusBadge = null!;

    // Content Panels for different states
    private Panel _pnlUnpaired = null!;
    private Label _lblPinCode = null!;
    private Label _lblPinExpires = null!;
    private Label _lblWaiting = null!;
    private Button _btnRefreshPin = null!;

    private Panel _pnlSuccess = null!;
    private Label _lblSuccessPhone = null!;
    private Label _lblSuccessCountdown = null!;
    private System.Windows.Forms.Timer? _autoTrayTimer;
    private int _countdownRemaining = 3;

    private Panel _pnlPaired = null!;
    private Label _lblPairedDeviceName = null!;
    private Label _lblPairedStatusDesc = null!;

    // Bottom Controls
    private CheckBox _chkAutoStart = null!;
    private Button _btnMinimize = null!;

    // Tray Menu Items
    private ToolStripMenuItem _trayStatusItem = null!;
    private ToolStripMenuItem _trayAutoStartItem = null!;

    public MainForm(AgentClient? agent = null, bool startMinimized = false, Action? onExitRequested = null)
    {
        _agent = agent ?? new AgentClient();
        _startMinimized = startMinimized;
        _onExitRequested = onExitRequested;
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        // Configure Form appearance
        Text = $"Remote Laptop Control - {_agent.DeviceId}";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 680);
        MinimumSize = new Size(500, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 15, 25);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = !startMinimized;

        if (startMinimized)
        {
            WindowState = FormWindowState.Minimized;
        }

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? CreateTrayIcon(online: false);
        }
        catch
        {
            Icon = CreateTrayIcon(online: false);
        }

        // 1. Initialize System Tray Icon
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(online: false),
            Text = $"Remote Laptop Control ({_agent.DeviceId})",
            Visible = true
        };

        BuildContextMenu();
        BuildUI();
        WireAgentEvents();

        // 2. Start Agent background connection
        Task.Run(async () =>
        {
            await _agent.StartAsync(_cts.Token);
        });

        // 3. If not yet paired, immediately request a pairing PIN on load
        if (!AgentClient.HasPairedToken)
        {
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                await _agent.RequestPairingCodeAsync();
            });
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_startMinimized)
        {
            Hide();
            ShowInTaskbar = false;
        }
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var titleItem = new ToolStripMenuItem($"Remote Laptop Control ({_agent.DeviceId})")
        {
            Enabled = false,
            Font = new Font(Font, FontStyle.Bold)
        };
        menu.Items.Add(titleItem);

        _trayStatusItem = new ToolStripMenuItem("Status: Connecting to relay...")
        {
            Enabled = false
        };
        menu.Items.Add(_trayStatusItem);

        menu.Items.Add(new ToolStripSeparator());

        var showItem = new ToolStripMenuItem("Open Agent Window", null, (_, _) =>
        {
            ShowAndRestore();
        });
        showItem.Font = new Font(Font, FontStyle.Bold);
        menu.Items.Add(showItem);

        var pairItem = new ToolStripMenuItem("Pair New Phone...", null, (_, _) =>
        {
            ShowAndRestore();
            SwitchToUnpairedState();
            Task.Run(async () => await _agent.RequestPairingCodeAsync());
        });
        menu.Items.Add(pairItem);

        var unpairItem = new ToolStripMenuItem("Reset Pairing / Unpair", null, (_, _) =>
        {
            if (MessageBox.Show("Are you sure you want to disconnect and unpair this laptop from your phone?", "Confirm Unpair", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _agent.Unpair();
                SwitchToUnpairedState();
                ShowAndRestore();
                Task.Run(async () => await _agent.RequestPairingCodeAsync());
            }
        });
        menu.Items.Add(unpairItem);

        menu.Items.Add(new ToolStripSeparator());

        _trayAutoStartItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = AutoStartManager.IsEnabled(),
            CheckOnClick = true
        };
        _trayAutoStartItem.CheckedChanged += (_, _) =>
        {
            if (_trayAutoStartItem.Checked)
            {
                AutoStartManager.Enable(arguments: "--tray");
                _chkAutoStart.Checked = true;
            }
            else
            {
                AutoStartManager.Disable();
                _chkAutoStart.Checked = false;
            }
        };
        menu.Items.Add(_trayAutoStartItem);

        var lockItem = new ToolStripMenuItem("Lock Workstation Now", null, (_, _) =>
        {
            SystemOperations.Lock();
        });
        menu.Items.Add(lockItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Agent", null, (_, _) =>
        {
            _isExiting = true;
            ExitApp();
        });
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;

        _notifyIcon.DoubleClick += (_, _) =>
        {
            ShowAndRestore();
        };

        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            ShowAndRestore();
        };
    }

    private void BuildUI()
    {
        SuspendLayout();

        // -------------------------------------------------------------
        // TOP HEADER PANEL
        // -------------------------------------------------------------
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = Color.FromArgb(17, 24, 39),
            Padding = new Padding(22, 14, 22, 14)
        };
        pnlHeader.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(31, 41, 55), 1f);
            e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
        };

        // Status Pill Badge (Docked cleanly to far right)
        _pnlStatusBadge = new Panel
        {
            Dock = DockStyle.Right,
            Width = 155,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };
        _pnlStatusBadge.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _pnlStatusBadge.Width;
            int h = _pnlStatusBadge.Height;
            int badgeY = Math.Max(0, (h - 32) / 2);
            using var path = GetRoundedPath(new Rectangle(0, badgeY, w - 1, 31), 15);
            using var brush = new SolidBrush(Color.FromArgb(24, 32, 47));
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(Color.FromArgb(45, 58, 82), 1f);
            e.Graphics.DrawPath(pen, path);
        };

        _lblStatusDot = new Label
        {
            Text = "●",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(245, 158, 11),
            Size = new Size(18, 22),
            Location = new Point(8, 17),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _lblStatusText = new Label
        {
            Text = "Connecting...",
            Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
            ForeColor = Color.FromArgb(245, 158, 11),
            Size = new Size(125, 24),
            Location = new Point(26, 16),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _pnlStatusBadge.Controls.Add(_lblStatusDot);
        _pnlStatusBadge.Controls.Add(_lblStatusText);

        var pnlHeaderTitles = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        var lblBrand = new Label
        {
            Text = "Remote Laptop Control",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(0, 4)
        };

        var lblSubBrand = new Label
        {
            Text = $"Device: {_agent.DeviceId}  •  Native Desktop Agent",
            Font = new Font("Segoe UI", 8.8f, FontStyle.Regular),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(1, 30)
        };

        pnlHeaderTitles.Controls.Add(lblBrand);
        pnlHeaderTitles.Controls.Add(lblSubBrand);

        pnlHeader.Controls.Add(pnlHeaderTitles);
        pnlHeader.Controls.Add(_pnlStatusBadge);
        Controls.Add(pnlHeader);

        // -------------------------------------------------------------
        // BOTTOM ACTION / SETTINGS PANEL
        // -------------------------------------------------------------
        var pnlBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 68,
            BackColor = Color.FromArgb(17, 24, 39),
            Padding = new Padding(22, 14, 22, 14)
        };
        pnlBottom.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(31, 41, 55), 1f);
            e.Graphics.DrawLine(pen, 0, 0, pnlBottom.Width, 0);
        };

        _chkAutoStart = new CheckBox
        {
            Text = "Start with Windows",
            ForeColor = Color.FromArgb(209, 213, 219),
            Font = new Font("Segoe UI", 9f),
            Dock = DockStyle.Left,
            AutoSize = true,
            Checked = AutoStartManager.IsEnabled()
        };
        _chkAutoStart.CheckedChanged += (_, _) =>
        {
            if (_chkAutoStart.Checked)
            {
                AutoStartManager.Enable(arguments: "--tray");
                _trayAutoStartItem.Checked = true;
            }
            else
            {
                AutoStartManager.Disable();
                _trayAutoStartItem.Checked = false;
            }
        };

        _btnMinimize = CreateStyledButton("Minimize to Tray ↓", Color.FromArgb(37, 99, 235), Color.White);
        _btnMinimize.Dock = DockStyle.Right;
        _btnMinimize.Width = 160;
        _btnMinimize.Height = 38;
        _btnMinimize.Click += (_, _) =>
        {
            HideToTray(showNotification: true);
        };

        pnlBottom.Controls.Add(_chkAutoStart);
        pnlBottom.Controls.Add(_btnMinimize);
        Controls.Add(pnlBottom);

        // -------------------------------------------------------------
        // CENTER CONTAINER FOR DYNAMIC STATE PANELS
        // -------------------------------------------------------------
        var pnlCenter = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 16),
            AutoScroll = true
        };

        // 1. UNPAIRED PANEL
        _pnlUnpaired = BuildUnpairedPanel();
        _pnlUnpaired.Dock = DockStyle.Fill;

        // 2. SUCCESS CELEBRATION PANEL
        _pnlSuccess = BuildSuccessPanel();
        _pnlSuccess.Dock = DockStyle.Fill;
        _pnlSuccess.Visible = false;

        // 3. PAIRED PANEL
        _pnlPaired = BuildPairedPanel();
        _pnlPaired.Dock = DockStyle.Fill;
        _pnlPaired.Visible = false;

        pnlCenter.Controls.Add(_pnlUnpaired);
        pnlCenter.Controls.Add(_pnlSuccess);
        pnlCenter.Controls.Add(_pnlPaired);
        Controls.Add(pnlCenter);

        // Determine starting visible panel
        if (AgentClient.HasPairedToken)
        {
            SwitchToPairedState(phoneId: _agent.PairedPhoneId ?? "Android Device");
        }
        else
        {
            SwitchToUnpairedState();
        }

        ResumeLayout(false);
    }

    private Panel BuildUnpairedPanel()
    {
        var panel = new Panel
        {
            BackColor = Color.FromArgb(17, 24, 39),
            Padding = new Padding(24, 20, 24, 20)
        };
        panel.Paint += (s, e) => DrawCardBorder(e.Graphics, panel.ClientRectangle);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 0: Title
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 1: Instruction
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 105f));       // 2: PIN Box
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 3: Expires Label
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));        // 4: Refresh Button
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));       // 5: Waiting Label

        var lblTitle = new Label
        {
            Text = "📱 Link Your Android Phone",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            AutoSize = true
        };
        table.Controls.Add(lblTitle, 0, 0);

        var lblInstruction = new Label
        {
            Text = "Open Remote Laptop Control on your phone, tap 'Pair Laptop', and enter this 6-digit numeric PIN:",
            Font = new Font("Segoe UI", 9.2f),
            ForeColor = Color.FromArgb(156, 163, 175),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            AutoSize = true
        };
        table.Controls.Add(lblInstruction, 0, 1);

        // Big PIN Display Box
        var pnlPinBox = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 14, 23),
            Margin = new Padding(0, 0, 0, 10)
        };
        pnlPinBox.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(56, 189, 248), 2f);
            using var path = GetRoundedPath(new Rectangle(2, 2, pnlPinBox.Width - 5, pnlPinBox.Height - 5), 12);
            e.Graphics.DrawPath(pen, path);
        };

        _lblPinCode = new Label
        {
            Text = "• • •  • • •",
            Font = new Font("Segoe UI", 36f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        pnlPinBox.Controls.Add(_lblPinCode);
        table.Controls.Add(pnlPinBox, 0, 2);

        _lblPinExpires = new Label
        {
            Text = "⏱️ Requesting pairing code from relay...",
            Font = new Font("Segoe UI", 8.8f, FontStyle.Italic),
            ForeColor = Color.FromArgb(156, 163, 175),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = true
        };
        table.Controls.Add(_lblPinExpires, 0, 3);

        _btnRefreshPin = CreateStyledButton("🔄 Refresh / Generate New PIN", Color.FromArgb(30, 41, 59), Color.White);
        _btnRefreshPin.Dock = DockStyle.Fill;
        _btnRefreshPin.Margin = new Padding(30, 0, 30, 12);
        _btnRefreshPin.Click += async (_, _) =>
        {
            _btnRefreshPin.Enabled = false;
            _lblPinCode.Text = "• • •  • • •";
            _lblPinExpires.Text = "⏱️ Requesting new code...";
            await _agent.RequestPairingCodeAsync();
            _btnRefreshPin.Enabled = true;
        };
        table.Controls.Add(_btnRefreshPin, 0, 4);

        _lblWaiting = new Label
        {
            Text = "⏳ Waiting for phone connection...\nOnce paired, this window will automatically minimize to your taskbar tray.",
            Font = new Font("Segoe UI", 8.6f),
            ForeColor = Color.FromArgb(107, 114, 128),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        table.Controls.Add(_lblWaiting, 0, 5);

        panel.Controls.Add(table);
        return panel;
    }

    private Panel BuildSuccessPanel()
    {
        var panel = new Panel
        {
            BackColor = Color.FromArgb(17, 24, 39),
            Padding = new Padding(24, 20, 24, 20)
        };
        panel.Paint += (s, e) => DrawCardBorder(e.Graphics, panel.ClientRectangle, Color.FromArgb(16, 185, 129));

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f)); // Icon
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Title
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Phone
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Countdown
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f)); // Button

        var lblIcon = new Label
        {
            Text = "🎉",
            Font = new Font("Segoe UI", 42f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        table.Controls.Add(lblIcon, 0, 0);

        var lblTitle = new Label
        {
            Text = "First Connection Established!",
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = Color.FromArgb(16, 185, 129),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 8),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = true
        };
        table.Controls.Add(lblTitle, 0, 1);

        _lblSuccessPhone = new Label
        {
            Text = "Successfully paired with your mobile phone.",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(209, 213, 219),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = true
        };
        table.Controls.Add(_lblSuccessPhone, 0, 2);

        _lblSuccessCountdown = new Label
        {
            Text = "🚀 Automatically minimizing to system tray in 3s...",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 16),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = true
        };
        table.Controls.Add(_lblSuccessCountdown, 0, 3);

        var btnMinimizeNow = CreateStyledButton("Minimize Now →", Color.FromArgb(16, 185, 129), Color.Black);
        btnMinimizeNow.Dock = DockStyle.Fill;
        btnMinimizeNow.Margin = new Padding(40, 0, 40, 0);
        btnMinimizeNow.Click += (_, _) =>
        {
            _autoTrayTimer?.Stop();
            SwitchToPairedState(_agent.PairedPhoneId ?? "Phone");
            HideToTray(showNotification: true);
        };
        table.Controls.Add(btnMinimizeNow, 0, 4);

        panel.Controls.Add(table);
        return panel;
    }

    private Panel BuildPairedPanel()
    {
        var panel = new Panel
        {
            BackColor = Color.FromArgb(17, 24, 39),
            Padding = new Padding(24, 20, 24, 20)
        };
        panel.Paint += (s, e) => DrawCardBorder(e.Graphics, panel.ClientRectangle, Color.FromArgb(16, 185, 129));

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Title
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Device Name
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Description
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f)); // Lock button
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f)); // Minimize button
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // Unpair button

        var lblTitle = new Label
        {
            Text = "🛡️ Laptop Guarded & Connected",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(16, 185, 129),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            AutoSize = true
        };
        table.Controls.Add(lblTitle, 0, 0);

        _lblPairedDeviceName = new Label
        {
            Text = "📱 Paired Phone: Active",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6),
            AutoSize = true
        };
        table.Controls.Add(_lblPairedDeviceName, 0, 1);

        _lblPairedStatusDesc = new Label
        {
            Text = "Your laptop is securely linked with your phone.\nRemote actions (LOCK, SLEEP, RESTART, SHUTDOWN) are actively guarded.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(156, 163, 175),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 18),
            AutoSize = true
        };
        table.Controls.Add(_lblPairedStatusDesc, 0, 2);

        var btnLockTest = CreateStyledButton("🔒 Quick Test: Lock Workstation", Color.FromArgb(30, 41, 59), Color.White);
        btnLockTest.Dock = DockStyle.Fill;
        btnLockTest.Margin = new Padding(0, 0, 0, 8);
        btnLockTest.Click += (_, _) =>
        {
            SystemOperations.Lock();
        };
        table.Controls.Add(btnLockTest, 0, 3);

        var btnMinimizeActive = CreateStyledButton("Minimize to Tray", Color.FromArgb(37, 99, 235), Color.White);
        btnMinimizeActive.Dock = DockStyle.Fill;
        btnMinimizeActive.Margin = new Padding(0, 0, 0, 8);
        btnMinimizeActive.Click += (_, _) =>
        {
            HideToTray(showNotification: true);
        };
        table.Controls.Add(btnMinimizeActive, 0, 4);

        var btnUnpair = CreateStyledButton("Disconnect / Unpair Phone", Color.FromArgb(220, 38, 38), Color.White);
        btnUnpair.Dock = DockStyle.Fill;
        btnUnpair.Click += (_, _) =>
        {
            if (MessageBox.Show("Are you sure you want to unpair this phone?", "Confirm Unpair", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _agent.Unpair();
                SwitchToUnpairedState();
                Task.Run(async () => await _agent.RequestPairingCodeAsync());
            }
        };
        table.Controls.Add(btnUnpair, 0, 5);

        panel.Controls.Add(table);
        return panel;
    }

    private void SafeInvoke(Action act)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(act);
            }
            else
            {
                act();
            }
        }
        catch { }
    }

    private void WireAgentEvents()
    {
        // Connection status changed
        _agent.ConnectionStatusChanged += isOnline =>
        {
            SafeInvoke(() =>
            {
                UpdateRelayStatus(isOnline);
            });
        };

        // Authentication failed (Unpaired)
        _agent.AuthenticationFailed += msg =>
        {
            SafeInvoke(() =>
            {
                _trayStatusItem.Text = "Status: Unpaired (Pair New Phone...)";
                _lblStatusDot.ForeColor = Color.FromArgb(245, 158, 11);
                _lblStatusText.Text = "Unpaired";
                _lblStatusText.ForeColor = Color.FromArgb(245, 158, 11);
                _notifyIcon.Icon = CreateTrayIcon(false);
            });
        };

        // Pairing code received
        _agent.PairingCodeReceived += (code, expiresIn) =>
        {
            SafeInvoke(() =>
            {
                // Format code with spaces for instant readability: "8 4 9  2 0 1"
                _lblPinCode.Text = code.Length == 6 
                    ? $"{code[..3]}  {code[3..]}" 
                    : code;

                _lblPinExpires.Text = $"⏱️ Code expires in {expiresIn / 60} minutes";
                _lblWaiting.Text = "⏳ Waiting for phone connection...\nEnter this PIN on your mobile app.";

                // If window is minimized to tray when PIN requested, pop balloon
                if (!Visible)
                {
                    _notifyIcon.ShowBalloonTip(
                        6000,
                        "Remote Laptop - Pairing PIN",
                        $"PIN: {code}\nEnter this code on your phone to link.",
                        ToolTipIcon.Info
                    );
                }
            });
        };

        // Pairing completed (FIRST CONNECTION FINISHES -> AUTO-TRAY)
        _agent.PairingCompleted += phoneId =>
        {
            SafeInvoke(() =>
            {
                // Celebrate on UI and start countdown to tray
                _lblSuccessPhone.Text = $"Linked successfully with: {phoneId}";
                _lblSuccessCountdown.Text = "🚀 Automatically minimizing to system tray in 3s...";
                _pnlUnpaired.Visible = false;
                _pnlPaired.Visible = false;
                _pnlSuccess.Visible = true;

                // Start 3-second auto-tray timer
                _countdownRemaining = 3;
                _autoTrayTimer?.Stop();
                _autoTrayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                _autoTrayTimer.Tick += (_, _) =>
                {
                    _countdownRemaining--;
                    if (_countdownRemaining > 0)
                    {
                        _lblSuccessCountdown.Text = $"🚀 Automatically minimizing to system tray in {_countdownRemaining}s...";
                    }
                    else
                    {
                        _autoTrayTimer.Stop();
                        SwitchToPairedState(phoneId);
                        HideToTray(showNotification: true);
                    }
                };
                _autoTrayTimer.Start();

                // Update tray menu and icon
                _trayStatusItem.Text = $"Status: Online (Paired with {phoneId})";
                _notifyIcon.Icon = CreateTrayIcon(true);
            });
        };

        // Command executed
        _agent.CommandExecuted += (cmd, success) =>
        {
            SafeInvoke(() =>
            {
                _notifyIcon.ShowBalloonTip(
                    4000,
                    "Remote Command Executed",
                    $"Phone triggered '{cmd}' (Success: {success})",
                    success ? ToolTipIcon.Info : ToolTipIcon.Warning
                );
            });
        };
    }

    private void UpdateRelayStatus(bool isOnline)
    {
        _notifyIcon.Icon = CreateTrayIcon(isOnline);
        _trayStatusItem.Text = isOnline 
            ? $"Status: Online {(!string.IsNullOrEmpty(_agent.PairedPhoneId) ? $"(Paired with {_agent.PairedPhoneId})" : "(Relay Connected)")}" 
            : "Status: Offline (Reconnecting...)";

        if (isOnline)
        {
            _lblStatusDot.ForeColor = Color.FromArgb(16, 185, 129);
            _lblStatusText.Text = "Relay Online";
            _lblStatusText.ForeColor = Color.FromArgb(16, 185, 129);
        }
        else
        {
            _lblStatusDot.ForeColor = Color.FromArgb(239, 68, 68);
            _lblStatusText.Text = "Reconnecting...";
            _lblStatusText.ForeColor = Color.FromArgb(239, 68, 68);
        }
    }

    private void SwitchToUnpairedState()
    {
        _autoTrayTimer?.Stop();
        _pnlSuccess.Visible = false;
        _pnlPaired.Visible = false;
        _pnlUnpaired.Visible = true;
    }

    private void SwitchToPairedState(string phoneId)
    {
        _autoTrayTimer?.Stop();
        _lblPairedDeviceName.Text = $"📱 Paired Phone: {phoneId}";
        _pnlUnpaired.Visible = false;
        _pnlSuccess.Visible = false;
        _pnlPaired.Visible = true;
    }

    private void HideToTray(bool showNotification)
    {
        Hide();
        ShowInTaskbar = false;
        if (showNotification)
        {
            _notifyIcon.ShowBalloonTip(
                3000,
                "Remote Laptop Active",
                "Agent is running in your taskbar system tray. Double-click the tray icon to reopen.",
                ToolTipIcon.Info
            );
        }
    }

    private void ShowAndRestore()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray(showNotification: true);
            return;
        }

        ExitApp();
        base.OnFormClosing(e);
    }

    private void ExitApp()
    {
        _autoTrayTimer?.Stop();
        _cts.Cancel();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _onExitRequested?.Invoke();
        Application.Exit();
    }

    private static Button CreateStyledButton(string text, Color bg, Color fg)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static void DrawCardBorder(Graphics g, Rectangle rect, Color? borderColor = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(borderColor ?? Color.FromArgb(31, 41, 55), 1.2f);
        using var path = GetRoundedPath(new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 3, rect.Height - 3), 12);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0) return path;

        int maxRadius = Math.Min(rect.Width / 2, rect.Height / 2);
        radius = Math.Max(1, Math.Min(radius, maxRadius));
        int d = radius * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon CreateTrayIcon(bool online)
    {
        try
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Outer circle badge
                var color = online ? Color.FromArgb(16, 185, 129) : Color.FromArgb(107, 114, 128);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, 2, 2, 28, 28);

                // Laptop icon drawing in crisp white
                using var pen = new Pen(Color.White, 2f);
                // Screen outline
                g.DrawRectangle(pen, 9, 8, 14, 10);
                // Keyboard base
                g.DrawLine(pen, 7, 21, 25, 21);
            }

            IntPtr hIcon = bmp.GetHicon();
            Icon icon = Icon.FromHandle(hIcon);
            Icon clone = (Icon)icon.Clone();
            DestroyIcon(hIcon);
            return clone;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}
