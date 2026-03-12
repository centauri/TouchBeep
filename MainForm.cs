using System.Reflection;

namespace TouchBeep;

public partial class MainForm : Form
{
    private TouchHook? _touchHook;
    private RawInputTouchListener? _rawInputListener;
    private NotifyIcon? _trayIcon;
    private bool _allowClose;
    private bool _soundEnabled = true;
    private int _frequencyHz = 800;
    private ToneGenerator.WaveType _waveType = ToneGenerator.WaveType.Sine;
    /// <summary>Timestamp of the last touch input (from hook or Raw Input). Used to distinguish a new touch from a finger still down.</summary>
    private DateTime _lastTouchInputTime = DateTime.MinValue;
    private readonly object _beepLock = new();
    /// <summary>Time gap (ms) since last input that is treated as a new touch. Shorter values allow more beeps; one beep per finger-hold is still enforced.</summary>
    private const int TouchGapMs = 60;
    /// <summary>Minimum interval (ms) between starting beeps. Must be at least the tone length (80 ms) so only one beep plays at a time and crackle or silence after rapid tapping is avoided.</summary>
    private const int MinBeepIntervalMs = 85;
    private DateTime _lastBeepStartTime = DateTime.MinValue;
    private List<string> _allowedProcesses = new();

    public MainForm()
    {
        InitializeComponent();
        LoadSettings();
        ApplyUiState();
        StartHook();
        _rawInputListener = new RawInputTouchListener(OnTouchOrClick);
        Load += OnFormLoad;
    }

    private void OnFormLoad(object? sender, EventArgs e)
    {
        if (IsHandleCreated && _rawInputListener != null)
            _rawInputListener.Register(Handle);
        if (Program.StartMinimized)
        {
            WindowState = FormWindowState.Minimized;
            Hide();
        }
    }

    private static string GetAppVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v == null) return "1.0.0";
        return v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
    }

    private void InitializeComponent()
    {
        Text = $"Touch Beep  v{GetAppVersion()}";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Size = new Size(350, 400);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = true;
        ShowInTaskbar = true;

        // ── Sound group ─────────────────────────────────────────────────
        var grpSound = new GroupBox
        {
            Text = "Sound",
            Location = new Point(10, 8),
            Size = new Size(318, 105)
        };

        var chkEnabled = new CheckBox
        {
            Text = "Enable sound on touch",
            Checked = true,
            AutoSize = true,
            Location = new Point(12, 22)
        };
        chkEnabled.CheckedChanged += (_, _) =>
        {
            _soundEnabled = chkEnabled.Checked;
            SaveSettings();
        };

        var lblFreq = new Label { Text = "Tone (Hz):", Location = new Point(12, 50), AutoSize = true };
        var numFreq = new NumericUpDown
        {
            Minimum = 200,
            Maximum = 4000,
            Value = 800,
            Increment = 50,
            Width = 80,
            Location = new Point(92, 48)
        };
        numFreq.ValueChanged += (_, _) =>
        {
            _frequencyHz = (int)numFreq.Value;
            SaveSettings();
        };

        var lblWave = new Label { Text = "Wave:", Location = new Point(12, 78), AutoSize = true };
        var cmbWave = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
            Location = new Point(92, 76)
        };
        cmbWave.Items.AddRange(new object[] { "Sine", "Square", "Triangle" });
        cmbWave.SelectedIndex = 0;
        cmbWave.SelectedIndexChanged += (_, _) =>
        {
            _waveType = (ToneGenerator.WaveType)cmbWave.SelectedIndex;
            SaveSettings();
        };

        grpSound.Controls.Add(chkEnabled);
        grpSound.Controls.Add(lblFreq);
        grpSound.Controls.Add(numFreq);
        grpSound.Controls.Add(lblWave);
        grpSound.Controls.Add(cmbWave);

        // ── Start with Windows ──────────────────────────────────────────
        var chkStartup = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Location = new Point(22, 120)
        };
        var lblStartupScope = new Label { AutoSize = true, Location = new Point(180, 122), ForeColor = Color.Gray };
        chkStartup.CheckedChanged += (_, _) =>
        {
            if (chkStartup.Checked)
            {
                if (RegistryStartup.EnableForAllUsers())
                    lblStartupScope.Text = "(all users)";
                else
                {
                    RegistryStartup.EnableForCurrentUser();
                    lblStartupScope.Text = "(current user)";
                }
            }
            else
            {
                RegistryStartup.Disable();
                lblStartupScope.Text = "";
            }
        };
        var lnkMode = new LinkLabel
        {
            AutoSize = true,
            Location = new Point(22, 146),
            Text = Program.IsElevated ? "Restart in user mode" : "Restart as administrator"
        };
        lnkMode.LinkClicked += (_, _) =>
        {
            bool targetUserMode = Program.IsElevated;
            if (!Program.RestartWithMode(targetUserMode))
            {
                MessageBox.Show(
                    "Could not restart in the selected mode.",
                    "Touch Beep", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ExitApplication();
        };

        // ── Filter apps group ───────────────────────────────────────────
        var grpFilter = new GroupBox
        {
            Text = "Filter apps (empty = all)",
            Location = new Point(10, 168),
            Size = new Size(318, 108)
        };

        var listFilter = new ListBox
        {
            Location = new Point(10, 20),
            Size = new Size(200, 72),
            SelectionMode = SelectionMode.One
        };
        var btnFilterAdd = new Button { Text = "Add...", Location = new Point(218, 20), Size = new Size(88, 26) };
        var btnFilterRemove = new Button { Text = "Remove", Location = new Point(218, 52), Size = new Size(88, 26) };
        btnFilterAdd.Click += (_, _) =>
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrEmpty(process.ProcessName))
                        names.Add(process.ProcessName);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
            var orderedNames = names
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            using var picker = new Form
            {
                Text = "Select process",
                Size = new Size(260, 340),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog
            };
            var lb = new ListBox { Location = new Point(12, 12), Size = new Size(220, 260), Sorted = true };
            foreach (var n in orderedNames) lb.Items.Add(n);
            var btnOk = new Button { Text = "OK", Location = new Point(12, 278), Size = new Size(70, 26), DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancel", Location = new Point(88, 278), Size = new Size(70, 26), DialogResult = DialogResult.Cancel };
            picker.AcceptButton = btnOk;
            picker.CancelButton = btnCancel;
            picker.Controls.Add(lb);
            picker.Controls.Add(btnOk);
            picker.Controls.Add(btnCancel);
            if (picker.ShowDialog(this) == DialogResult.OK && lb.SelectedItem is string name)
            {
                if (!_allowedProcesses.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
                {
                    _allowedProcesses.Add(name);
                    listFilter.Items.Add(name);
                    Settings.Default.AllowedProcesses = new List<string>(_allowedProcesses);
                }
            }
        };
        btnFilterRemove.Click += (_, _) =>
        {
            if (listFilter.SelectedIndex >= 0)
            {
                _allowedProcesses.RemoveAt(listFilter.SelectedIndex);
                listFilter.Items.RemoveAt(listFilter.SelectedIndex);
                Settings.Default.AllowedProcesses = new List<string>(_allowedProcesses);
            }
        };

        grpFilter.Controls.Add(listFilter);
        grpFilter.Controls.Add(btnFilterAdd);
        grpFilter.Controls.Add(btnFilterRemove);

        // ── Bottom button row ───────────────────────────────────────────
        var btnTest = new Button
        {
            Text = "Test sound",
            Location = new Point(10, 286),
            Size = new Size(100, 30)
        };
        btnTest.Click += (_, _) =>
        {
            if (_soundEnabled)
                ToneGenerator.PlayAsync(_frequencyHz, _waveType);
        };

        var btnTouchDevices = new Button
        {
            Text = "Touch Devices...",
            Location = new Point(118, 286),
            Size = new Size(120, 30)
        };
        btnTouchDevices.Click += (_, _) => ShowTouchDevicesDialog();
        var btnExit = new Button
        {
            Text = "Exit App",
            Location = new Point(246, 286),
            Size = new Size(82, 30)
        };
        btnExit.Click += (_, _) => ExitApplication();

        var lblCopyright = new Label
        {
            Text = $"v{GetAppVersion()}  " + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "© ictadmiraal"),
            Location = new Point(10, 324),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font(Font.FontFamily, Font.Size - 0.5f)
        };

        // ── Add to form ─────────────────────────────────────────────────
        Controls.Add(grpSound);
        Controls.Add(chkStartup);
        Controls.Add(lblStartupScope);
        Controls.Add(lnkMode);
        Controls.Add(grpFilter);
        Controls.Add(btnTest);
        Controls.Add(btnTouchDevices);
        Controls.Add(btnExit);
        Controls.Add(lblCopyright);

        // ── System tray ─────────────────────────────────────────────────
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Touch Beep - tap to beep",
            Visible = true
        };
        var menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("Show");
        showItem.Click += (_, _) => { Show(); WindowState = FormWindowState.Normal; BringToFront(); };
        var modeItem = new ToolStripMenuItem(Program.IsElevated ? "Restart in user mode" : "Restart as administrator");
        modeItem.Click += (_, _) =>
        {
            bool targetUserMode = Program.IsElevated;
            if (Program.RestartWithMode(targetUserMode))
                ExitApplication();
            else
                MessageBox.Show("Could not restart in the selected mode.", "Touch Beep", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(showItem);
        menu.Items.Add(modeItem);
        menu.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; BringToFront(); };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                _trayIcon?.ShowBalloonTip(2000, "Touch Beep", "Minimized to tray. Double-click to open.", ToolTipIcon.Info);
            }
        };

        Closing += (s, e) =>
        {
            var ev = (FormClosingEventArgs)e;
            if (!_allowClose && ev.CloseReason == CloseReason.UserClosing)
            {
                ev.Cancel = true;
                WindowState = FormWindowState.Minimized;
                Hide();
            }
        };

        // Store for LoadSettings
        _chkEnabled = chkEnabled;
        _numFreq = numFreq;
        _cmbWave = cmbWave;
        _chkStartup = chkStartup;
        _lblStartupScope = lblStartupScope;
        _listFilter = listFilter;
    }

    private CheckBox? _chkEnabled;
    private Label? _lblStartupScope;
    private ListBox? _listFilter;
    private NumericUpDown? _numFreq;
    private ComboBox? _cmbWave;
    private CheckBox? _chkStartup;

    private void LoadSettings()
    {
        try
        {
            _soundEnabled = Settings.Default.SoundEnabled;
            _frequencyHz = Settings.Default.FrequencyHz;
            if (_frequencyHz < 200) _frequencyHz = 200;
            if (_frequencyHz > 4000) _frequencyHz = 4000;
            _waveType = (ToneGenerator.WaveType)(Settings.Default.WaveType < 0 ? 0 : Settings.Default.WaveType > 2 ? 2 : Settings.Default.WaveType);
            _allowedProcesses = new List<string>(Settings.Default.AllowedProcesses);
        }
        catch { }
    }

    private void ApplyUiState()
    {
        _chkEnabled!.Checked = _soundEnabled;
        _numFreq!.Value = _frequencyHz;
        _cmbWave!.SelectedIndex = (int)_waveType;
        _chkStartup!.Checked = RegistryStartup.IsEnabled;
        _lblStartupScope!.Text = RegistryStartup.IsEnabledForAllUsers ? "(all users)" : (RegistryStartup.IsEnabledForCurrentUser ? "(current user)" : "");
        _listFilter!.Items.Clear();
        foreach (var p in _allowedProcesses) _listFilter.Items.Add(p);
    }

    private void SaveSettings()
    {
        try
        {
            Settings.Default.SoundEnabled = _soundEnabled;
            Settings.Default.FrequencyHz = _frequencyHz;
            Settings.Default.WaveType = (int)_waveType;
            Settings.Default.AllowedProcesses = new List<string>(_allowedProcesses);
        }
        catch { }
    }

    private void StartHook()
    {
        try
        {
            _touchHook?.Dispose();
            _touchHook = new TouchHook(OnTouchOrClick);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not install touch/click hook: " + ex.Message, "Touch Beep", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnTouchOrClick()
    {
        if (!_soundEnabled) return;
        var now = DateTime.UtcNow;
        bool isNewTouch;
        lock (_beepLock)
        {
            double gapMs = _lastTouchInputTime == DateTime.MinValue ? double.MaxValue : (now - _lastTouchInputTime).TotalMilliseconds;
            _lastTouchInputTime = now;
            isNewTouch = gapMs >= TouchGapMs;
        }
        if (!isNewTouch) return;
        lock (_beepLock)
        {
            if ((now - _lastBeepStartTime).TotalMilliseconds < MinBeepIntervalMs) return;
            _lastBeepStartTime = now;
        }
        if (!ProcessFilter.ShouldBeep(_allowedProcesses)) return;
        try
        {
            ToneGenerator.PlayAsync(_frequencyHz, _waveType);
        }
        catch { }
    }

    private void ShowTouchDevicesDialog()
    {
        using var dlg = new Form
        {
            Text = "Touch Devices",
            Size = new Size(480, 310),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var lblInfo = new Label
        {
            Text = "Connected touch screens. Select a device to enable or disable it.",
            Location = new Point(14, 10),
            AutoSize = true
        };

        var lv = new ListView
        {
            Location = new Point(14, 32),
            Size = new Size(438, 160),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        lv.Columns.Add("#", 32);
        lv.Columns.Add("Device", 170);
        lv.Columns.Add("Monitor", 130);
        lv.Columns.Add("Status", 80);

        var btnToggle = new Button { Text = "Disable", Location = new Point(14, 200), Size = new Size(90, 28), Enabled = false };
        var btnRefresh = new Button { Text = "Refresh", Location = new Point(112, 200), Size = new Size(80, 28) };
        var btnClose = new Button { Text = "Close", Location = new Point(362, 200), Size = new Size(90, 28), DialogResult = DialogResult.Cancel };
        dlg.CancelButton = btnClose;

        var lblWarn = new Label
        {
            Text = "Changes are system-wide and persist across reboots.",
            Location = new Point(14, 238),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font(Font.FontFamily, Font.Size - 0.5f)
        };

        void RefreshList()
        {
            lv.Items.Clear();
            btnToggle.Enabled = false;
            var devices = TouchDeviceManager.GetTouchDevices();
            if (devices.Count == 0)
            {
                lv.Items.Add(new ListViewItem(new[] { "", "No touch screens found", "", "" }));
                return;
            }
            foreach (var d in devices)
            {
                string mon = string.IsNullOrEmpty(d.MonitorName) ? "Unknown" : d.MonitorName;
                if (d.IsPrimaryMonitor) mon += " (primary)";
                var item = new ListViewItem(new[]
                {
                    d.Index.ToString(),
                    d.Description,
                    mon,
                    d.IsEnabled ? "Enabled" : "Disabled"
                }) { Tag = d };
                if (!d.IsEnabled)
                    item.ForeColor = Color.Gray;
                lv.Items.Add(item);
            }
        }

        RefreshList();

        lv.SelectedIndexChanged += (_, _) =>
        {
            if (lv.SelectedItems.Count > 0 && lv.SelectedItems[0].Tag is TouchDeviceInfo d)
            {
                btnToggle.Enabled = true;
                btnToggle.Text = d.IsEnabled ? "Disable" : "Enable";
            }
            else
            {
                btnToggle.Enabled = false;
            }
        };

        btnToggle.Click += (_, _) =>
        {
            if (lv.SelectedItems.Count == 0 || lv.SelectedItems[0].Tag is not TouchDeviceInfo d) return;
            bool newState = !d.IsEnabled;
            if (!newState)
            {
                var r = MessageBox.Show(
                    $"Disable touch on \"{d.Description}\"?\n\n" +
                    "This is system-wide and persists across reboots.\n" +
                    "Use this dialog or --enable-touch to re-enable.",
                    "Confirm Disable", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
            if (TouchDeviceManager.SetDeviceEnabled(d.InstanceId, newState))
            {
                RefreshList();
            }
            else
            {
                string verb = newState ? "enable" : "disable";
                MessageBox.Show(
                    $"Failed to {verb} device.\nMake sure the app is running as administrator.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        btnRefresh.Click += (_, _) => RefreshList();

        dlg.Controls.Add(lblInfo);
        dlg.Controls.Add(lv);
        dlg.Controls.Add(btnToggle);
        dlg.Controls.Add(btnRefresh);
        dlg.Controls.Add(btnClose);
        dlg.Controls.Add(lblWarn);
        dlg.ShowDialog(this);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == RawInputTouchListener.WmInput && _rawInputListener != null)
            _rawInputListener.ProcessInput(m.LParam);
        base.WndProc(ref m);
    }

    private void ExitApplication()
    {
        _allowClose = true;
        if (_trayIcon != null)
            _trayIcon.Visible = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _touchHook?.Dispose();
            _rawInputListener?.Dispose();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
