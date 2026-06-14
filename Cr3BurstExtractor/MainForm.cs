using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cr3BurstExtractor.Helpers;
using Cr3BurstExtractor.Managers;

namespace Cr3BurstExtractor;

public sealed class MainForm : Form
{
    // ---- palette ----------------------------------------------------------
    static readonly Color AccentBlue      = Color.FromArgb( 30, 110, 200);
    static readonly Color AccentBlueHover = Color.FromArgb( 45, 130, 220);
    static readonly Color AccentBlueDown  = Color.FromArgb( 20,  95, 175);
    static readonly Color BodyText        = Color.FromArgb( 38,  42,  50);
    static readonly Color MutedText       = Color.FromArgb(110, 115, 125);
    static readonly Color SectionHeader   = Color.FromArgb( 55,  65,  80);
    static readonly Color SurfaceBg       = Color.FromArgb(248, 249, 251);
    static readonly Color LogBg           = Color.FromArgb(252, 253, 254);
    static readonly Color LogBorder       = Color.FromArgb(218, 222, 230);

    // ---- layout constants -------------------------------------------------
    const int FormMargin     = 24;
    const int SectionGap     = 18;
    const int LabelToInputGap = 6;
    const int InputBrowseGap = 10;
    const int InputHeight    = 28;
    const int BrowseButtonW  = 96;
    const int ExtractButtonW = 130;
    const int ExtractButtonH = 38;
    const int ProgressBarH   = 22;
    const int ProgressLabelW = 150; // wide enough for "999999 / 999999" (6-digit totals)

    // ---- controls ---------------------------------------------------------
    readonly TextBox _scanDirBox;
    readonly TextBox _backupDirBox;
    readonly Button _scanBrowse;
    readonly Button _backupBrowse;
    readonly Button _extractButton;
    readonly CheckBox _moveOriginalsCheckbox;
    readonly ProgressBar _progressBar;
    readonly Label _progressLabel;
    readonly Label _burstStatusLabel;
    readonly TextBox _logBox;

    CancellationTokenSource? _cts;
    bool _running;

    public MainForm()
    {
        Text = "CR3 Burst Extractor";
        ClientSize = new Size(860, 600);
        MinimumSize = new Size(680, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = LogoRenderer.CreateIcon(64);
        BackColor = SurfaceBg;
        Font = new Font("Segoe UI", 9.5f);
        ForeColor = BodyText;

        var menu = BuildMenu();
        MainMenuStrip = menu;
        Controls.Add(menu);

        // -----------------------------------------------------------------
        // Build the content layout in a single host panel. We size the panel
        // up-front (Dock = Fill positions it correctly relative to the menu)
        // BEFORE adding children, so anchored offsets are computed against
        // the real content area.
        // -----------------------------------------------------------------
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        Controls.Add(content);

        // Force the panel to settle into its docked size now, before we
        // populate it. Without this, the panel still reports 0x0 when we
        // add anchored children and their offsets become invalid.
        content.PerformLayout();
        int contentW = content.ClientSize.Width;
        int contentH = content.ClientSize.Height;

        int leftEdge  = FormMargin;
        int rightEdge = contentW - FormMargin;
        int inputW    = rightEdge - BrowseButtonW - InputBrowseGap - leftEdge;

        var sectionFont  = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        var inputFont    = new Font("Segoe UI", 10f);
        var extractFont  = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        var monoFont     = new Font("Consolas", 9.5f);

        int y = FormMargin;

        // ---- Scan folder section ----------------------------------------
        var scanLabel = new Label
        {
            Text = "Scan folder",
            Font = sectionFont,
            ForeColor = SectionHeader,
            AutoSize = true,
            Left = leftEdge,
            Top = y
        };
        y += scanLabel.PreferredHeight + LabelToInputGap;

        _scanDirBox = new TextBox
        {
            Left = leftEdge,
            Top = y,
            Width = inputW,
            Font = inputFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = UserSettings.ScanFolder ?? ""
        };

        _scanBrowse = new Button
        {
            Text = "Browse...",
            Font = inputFont,
            Width = BrowseButtonW,
            Height = InputHeight,
            Top = y - 1,
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _scanBrowse.Left = rightEdge - BrowseButtonW;
        _scanBrowse.Click += (_, _) => PickFolder(_scanDirBox, "Select folder to scan for .CR3 files");

        y += InputHeight + SectionGap;

        // ---- Backup folder section --------------------------------------
        // Section header is itself the on/off toggle: when unchecked the
        // textbox + browse below grey out and originals stay where they are.
        _moveOriginalsCheckbox = new CheckBox
        {
            Text = "Move originals to backup folder",
            Font = sectionFont,
            ForeColor = SectionHeader,
            AutoSize = true,
            Left = leftEdge,
            Top = y,
            Checked = UserSettings.MoveOriginalsToBackup
        };
        _moveOriginalsCheckbox.CheckedChanged += (_, _) => UpdateBackupControlsEnabled();
        y += _moveOriginalsCheckbox.PreferredSize.Height + LabelToInputGap;

        _backupDirBox = new TextBox
        {
            Left = leftEdge,
            Top = y,
            Width = inputW,
            Font = inputFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = UserSettings.BackupFolder ?? ""
        };

        _backupBrowse = new Button
        {
            Text = "Browse...",
            Font = inputFont,
            Width = BrowseButtonW,
            Height = InputHeight,
            Top = y - 1,
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _backupBrowse.Left = rightEdge - BrowseButtonW;
        _backupBrowse.Click += (_, _) => PickFolder(_backupDirBox, "Select folder to move original burst files into");

        y += InputHeight + SectionGap + 6;

        // ---- Action row: [progress bar  N / M]              [Extract] ---
        int actionRowTop = y;

        _extractButton = new Button
        {
            Text = "Extract",
            Font = extractFont,
            Width = ExtractButtonW,
            Height = ExtractButtonH,
            Top = actionRowTop,
            BackColor = AccentBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _extractButton.FlatAppearance.BorderSize = 0;
        _extractButton.FlatAppearance.MouseOverBackColor = AccentBlueHover;
        _extractButton.FlatAppearance.MouseDownBackColor = AccentBlueDown;
        _extractButton.Left = rightEdge - ExtractButtonW;
        _extractButton.Click += OnExtractAsync;

        int progressBarRight = _extractButton.Left - InputBrowseGap - ProgressLabelW - InputBrowseGap;
        _progressBar = new ProgressBar
        {
            Left = leftEdge,
            Top = actionRowTop + (ExtractButtonH - ProgressBarH) / 2,
            Width = progressBarRight - leftEdge,
            Height = ProgressBarH,
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _progressLabel = new Label
        {
            Left = _progressBar.Right + InputBrowseGap,
            Top = actionRowTop + (ExtractButtonH - 20) / 2,
            Width = ProgressLabelW,
            Height = 20,
            Text = "",
            Font = inputFont,
            ForeColor = MutedText,
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        y += ExtractButtonH + 8;

        // ---- Burst counter row (sub-label under the progress row) ------
        _burstStatusLabel = new Label
        {
            Left = leftEdge,
            Top = y,
            Width = rightEdge - leftEdge,
            Height = 20,
            Text = "",
            Font = new Font("Segoe UI", 9f),
            ForeColor = MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        y += 20 + SectionGap + 4;

        // ---- Log section ------------------------------------------------
        var logLabel = new Label
        {
            Text = "Log",
            Font = sectionFont,
            ForeColor = SectionHeader,
            AutoSize = true,
            Left = leftEdge,
            Top = y
        };
        y += logLabel.PreferredHeight + LabelToInputGap;

        _logBox = new TextBox
        {
            Left = leftEdge,
            Top = y,
            Width = rightEdge - leftEdge,
            Height = contentH - y - FormMargin,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = monoFont,
            BackColor = LogBg,
            ForeColor = BodyText,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        content.Controls.AddRange(new Control[]
        {
            scanLabel,             _scanDirBox,   _scanBrowse,
            _moveOriginalsCheckbox, _backupDirBox, _backupBrowse,
            _progressBar, _progressLabel, _extractButton,
            _burstStatusLabel,
            logLabel, _logBox
        });

        UpdateBackupControlsEnabled();

        FormClosing += (_, _) =>
        {
            UserSettings.ScanFolder = _scanDirBox.Text;
            UserSettings.BackupFolder = _backupDirBox.Text;
            UserSettings.MoveOriginalsToBackup = _moveOriginalsCheckbox.Checked;
            UserSettings.Save();
        };
    }

    /// <summary>
    /// Greys out the backup folder textbox + Browse button when the user has
    /// chosen to leave originals in place. Also called by <see cref="SetBusy"/>
    /// so extraction-time enable state respects both the running flag and the
    /// checkbox.
    /// </summary>
    void UpdateBackupControlsEnabled()
    {
        bool moveEnabled = _moveOriginalsCheckbox.Checked;
        _backupDirBox.Enabled = moveEnabled && !_running;
        _backupBrowse.Enabled = moveEnabled && !_running;
    }

    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.White,
            Renderer = new ToolStripProfessionalRenderer(new AccentMenuColors()) { RoundedEdges = false }
        };

        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close())
        {
            ShortcutKeys = Keys.Alt | Keys.F4
        });

        var settingsMenu = BuildSettingsMenu();
        var serviceMenu  = BuildServiceMenu();

        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("&Help", null, (_, _) =>
        {
            using var help = new HelpForm();
            help.ShowDialog(this);
        })
        { ShortcutKeys = Keys.F1 });
        helpMenu.DropDownItems.Add(new ToolStripSeparator());
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("&About", null, (_, _) =>
        {
            using var about = new SplashForm(isAbout: true);
            about.ShowDialog(this);
        }));

        menu.Items.Add(fileMenu);
        menu.Items.Add(settingsMenu);
        menu.Items.Add(serviceMenu);
        menu.Items.Add(helpMenu);
        return menu;
    }

    ToolStripMenuItem BuildSettingsMenu()
    {
        var settingsMenu = new ToolStripMenuItem("&Settings");

        var autoExtract = new ToolStripMenuItem("&Auto-extract new files in scan folder")
        {
            CheckOnClick = true,
            Checked = UserSettings.AutoExtractOnNewFiles,
            ToolTipText = "When enabled, the background service watches the scan folder " +
                          "and extracts new burst CR3s automatically. Requires the service " +
                          "to be installed and running (see the Service menu).",
        };
        autoExtract.CheckedChanged += (_, _) =>
        {
            UserSettings.AutoExtractOnNewFiles = autoExtract.Checked;
            UserSettings.Save();

            if (!autoExtract.Checked) return;
            var state = ServiceStatus.QueryState();
            if (state == ServiceState.NotInstalled)
            {
                MessageBox.Show(this,
                    "Auto-extract is on, but the background service isn't installed yet.\n\n" +
                    "Use Service → Install service to install it, then Start service.",
                    "Service not installed",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (state == ServiceState.Stopped)
            {
                MessageBox.Show(this,
                    "Auto-extract is on, but the background service isn't running.\n\n" +
                    "Use Service → Start service to start it.",
                    "Service not running",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        settingsMenu.DropDownItems.Add(autoExtract);

        var showNotifications = new ToolStripMenuItem("Show Windows &notification on auto-extract")
        {
            CheckOnClick = true,
            Checked = UserSettings.ShowNotifications,
            ToolTipText = "When the service auto-extracts a burst, pop a Windows toast " +
                          "(\"Extracted N frames from xyz.CR3\"). Turn off if the toasts " +
                          "are noisy during a long camera offload.",
        };
        showNotifications.CheckedChanged += (_, _) =>
        {
            UserSettings.ShowNotifications = showNotifications.Checked;
            UserSettings.Save();
        };
        settingsMenu.DropDownItems.Add(showNotifications);

        return settingsMenu;
    }

    ToolStripMenuItem BuildServiceMenu()
    {
        var serviceMenu = new ToolStripMenuItem("Ser&vice");

        var install   = new ToolStripMenuItem("&Install service…",   null, (_, _) => RunSelfElevated("--install"));
        var uninstall = new ToolStripMenuItem("&Uninstall service…", null, (_, _) => RunSelfElevated("--uninstall"));
        var start     = new ToolStripMenuItem("&Start service",           null, (_, _) => RunSelfElevated("--start"));
        var stop      = new ToolStripMenuItem("S&top service",            null, (_, _) => RunSelfElevated("--stop"));

        serviceMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            install, uninstall, new ToolStripSeparator(), start, stop,
        });

        serviceMenu.DropDownOpening += (_, _) =>
        {
            var state = ServiceStatus.QueryState();
            bool installed = state != ServiceState.NotInstalled;
            install.Enabled   = !installed;
            uninstall.Enabled = installed;
            start.Enabled     = installed && state != ServiceState.Running && state != ServiceState.StartPending;
            stop.Enabled      = installed && state != ServiceState.Stopped && state != ServiceState.StopPending;
        };

        return serviceMenu;
    }

    void RunSelfElevated(string subcommand)
    {
        try
        {
            string exe = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot determine executable path.");
            var psi = new System.Diagnostics.ProcessStartInfo(exe, subcommand)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC — leave state as-is.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to run {subcommand}: {ex.Message}",
                "Service command failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    sealed class AccentMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected               => Color.FromArgb(232, 240, 252);
        public override Color MenuItemBorder                 => Color.FromArgb(180, 200, 230);
        public override Color MenuItemSelectedGradientBegin  => Color.FromArgb(232, 240, 252);
        public override Color MenuItemSelectedGradientEnd    => Color.FromArgb(232, 240, 252);
        public override Color MenuItemPressedGradientBegin   => Color.FromArgb(216, 230, 248);
        public override Color MenuItemPressedGradientEnd     => Color.FromArgb(216, 230, 248);
        public override Color ToolStripBorder                => LogBorder;
        public override Color MenuBorder                     => Color.FromArgb(200, 205, 215);
    }

    void PickFolder(TextBox target, string description)
    {
        using var dlg = new FolderBrowserDialog { Description = description };
        if (!string.IsNullOrWhiteSpace(target.Text) && Directory.Exists(target.Text))
            dlg.SelectedPath = target.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    async void OnExtractAsync(object? sender, EventArgs e)
    {
        if (_running)
        {
            _cts?.Cancel();
            _extractButton.Text = "Stopping...";
            _extractButton.Enabled = false;
            return;
        }

        string scanDir = _scanDirBox.Text.Trim();
        string backupDir = _backupDirBox.Text.Trim();
        bool moveOriginals = _moveOriginalsCheckbox.Checked;

        if (string.IsNullOrEmpty(scanDir) || !Directory.Exists(scanDir))
        {
            MessageBox.Show(this, "Select a valid scan folder.", "Missing scan folder",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (moveOriginals && string.IsNullOrEmpty(backupDir))
        {
            MessageBox.Show(this,
                "Select a backup folder, or uncheck \"Move originals to backup folder\" to leave them in place.",
                "Missing backup folder",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        _running = true;
        SetBusy(true);
        _logBox.Clear();
        _progressBar.Value = 0;
        _progressBar.Maximum = 1;
        _progressLabel.Text = "";
        _burstStatusLabel.Text = "";

        var progress = new Progress<(int processed, int total, int burstsFound, int framesExtracted)>(p =>
        {
            if (p.total <= 0)
            {
                _progressBar.Maximum = 1;
                _progressBar.Value = 0;
                _progressLabel.Text = "0 / 0";
                _burstStatusLabel.Text = "0 burst files found";
                return;
            }
            if (_progressBar.Maximum != p.total) _progressBar.Maximum = p.total;
            _progressBar.Value = Math.Min(p.processed, p.total);
            _progressLabel.Text = $"{p.processed} / {p.total}";
            _burstStatusLabel.Text =
                $"Bursts found: {p.burstsFound}    ·    Files extracted: {p.framesExtracted}";
        });

        var prev = Console.Out;
        Console.SetOut(new TextBoxWriter(_logBox));

        try
        {
            if (moveOriginals) Directory.CreateDirectory(backupDir);
            var token = _cts.Token;
            await Task.Run(() => ProcessDirectory(scanDir, backupDir, moveOriginals, token, progress));
        }
        catch (Exception ex)
        {
            AppendLog($"{Environment.NewLine}ERROR: {ex.Message}{Environment.NewLine}");
            MessageBox.Show(this, ex.Message, "Failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Console.SetOut(prev);
            _running = false;
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    static void ProcessDirectory(string scanDir, string backupDir, bool moveOriginals,
                                  CancellationToken token,
                                  IProgress<(int processed, int total, int burstsFound, int framesExtracted)> progress)
    {
        // When moving originals, exclude anything already inside the backup folder
        // so a re-run doesn't reprocess previously archived rolls. When NOT moving,
        // backupDir may be empty and there's nothing to exclude.
        string? fullBackup = moveOriginals && !string.IsNullOrEmpty(backupDir)
            ? Path.GetFullPath(backupDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : null;

        Console.WriteLine($"Counting .CR3 files under {scanDir} ...");

        var files = Directory.EnumerateFiles(scanDir, "*.CR3", SearchOption.AllDirectories)
            .Where(f => fullBackup == null || !IsUnder(f, fullBackup))
            .ToList();

        int total = files.Count;
        int burstsFound = 0;
        int framesExtracted = 0;
        progress.Report((0, total, burstsFound, framesExtracted));

        Console.WriteLine($"Found {total} .CR3 file(s).");
        Console.WriteLine();

        int extracted = 0, skipped = 0, cached = 0, errors = 0;
        int processed = 0;
        bool stopped = false;

        foreach (var file in files)
        {
            if (token.IsCancellationRequested)
            {
                Console.WriteLine("Stopped by user before next file.");
                stopped = true;
                break;
            }
            try
            {
                var result = AutoExtractor.ProcessFile(file, moveOriginals, backupDir, Console.WriteLine);
                switch (result.Outcome)
                {
                    case AutoExtractOutcome.Cached:
                        cached++;
                        break;
                    case AutoExtractOutcome.SkippedNonBurst:
                        skipped++;
                        break;
                    case AutoExtractOutcome.Extracted:
                        burstsFound++;
                        framesExtracted += result.FrameCount;
                        extracted++;
                        break;
                    case AutoExtractOutcome.Error:
                        errors++;
                        break;
                }
            }
            finally
            {
                processed++;
                progress.Report((processed, total, burstsFound, framesExtracted));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {extracted} burst(s) extracted, " +
                          $"{skipped} newly skipped, {cached} cached (already known non-burst), " +
                          $"{errors} error(s)" + (stopped ? " (stopped by user)." : "."));
    }

    static bool IsUnder(string filePath, string fullDir)
    {
        string fullFile = Path.GetFullPath(filePath);
        return fullFile.StartsWith(fullDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullFile.StartsWith(fullDir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    void SetBusy(bool busy)
    {
        _extractButton.Text = busy ? "Stop" : "Extract";
        _extractButton.Enabled = true;
        _scanBrowse.Enabled = !busy;
        _scanDirBox.Enabled = !busy;
        _moveOriginalsCheckbox.Enabled = !busy;
        UpdateBackupControlsEnabled(); // respects both _running and the checkbox state
        UseWaitCursor = busy;
    }

    void AppendLog(string text)
    {
        if (_logBox.InvokeRequired)
        {
            _logBox.BeginInvoke(new Action(() => AppendLog(text)));
            return;
        }
        _logBox.AppendText(text);
    }

    sealed class TextBoxWriter : TextWriter
    {
        readonly TextBox _target;
        public TextBoxWriter(TextBox target) { _target = target; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => Append(value.ToString());
        public override void Write(string? value) { if (value != null) Append(value); }
        public override void WriteLine(string? value) => Append((value ?? string.Empty) + Environment.NewLine);

        void Append(string s)
        {
            if (_target.IsDisposed) return;
            if (_target.InvokeRequired)
                _target.BeginInvoke(new Action(() => { if (!_target.IsDisposed) _target.AppendText(s); }));
            else
                _target.AppendText(s);
        }
    }
}
