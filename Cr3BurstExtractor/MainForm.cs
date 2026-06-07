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
        var backupLabel = new Label
        {
            Text = "Backup folder",
            Font = sectionFont,
            ForeColor = SectionHeader,
            AutoSize = true,
            Left = leftEdge,
            Top = y
        };
        y += backupLabel.PreferredHeight + LabelToInputGap;

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
            scanLabel,   _scanDirBox,   _scanBrowse,
            backupLabel, _backupDirBox, _backupBrowse,
            _progressBar, _progressLabel, _extractButton,
            _burstStatusLabel,
            logLabel, _logBox
        });

        FormClosing += (_, _) =>
        {
            UserSettings.ScanFolder = _scanDirBox.Text;
            UserSettings.BackupFolder = _backupDirBox.Text;
            UserSettings.Save();
        };
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
        menu.Items.Add(helpMenu);
        return menu;
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

        if (string.IsNullOrEmpty(scanDir) || !Directory.Exists(scanDir))
        {
            MessageBox.Show(this, "Select a valid scan folder.", "Missing scan folder",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrEmpty(backupDir))
        {
            MessageBox.Show(this, "Select a backup folder.", "Missing backup folder",
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
            Directory.CreateDirectory(backupDir);
            var token = _cts.Token;
            await Task.Run(() => ProcessDirectory(scanDir, backupDir, token, progress));
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

    static void ProcessDirectory(string scanDir, string backupDir, CancellationToken token,
                                  IProgress<(int processed, int total, int burstsFound, int framesExtracted)> progress)
    {
        string fullBackup = Path.GetFullPath(backupDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Console.WriteLine($"Counting .CR3 files under {scanDir} ...");

        var files = Directory.EnumerateFiles(scanDir, "*.CR3", SearchOption.AllDirectories)
            .Where(f => !IsUnder(f, fullBackup))
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
                var info = new FileInfo(file);
                if (NonBurstCache.IsKnownNonBurst(file, info))
                {
                    // Previously confirmed single-frame — skip silently to keep the
                    // log readable. Still counts toward progress.
                    cached++;
                    continue;
                }

                int frames = BurstExtractor.GetFrameCount(file);
                if (frames <= 1)
                {
                    Console.WriteLine($"SKIP ({frames} frame): {file}");
                    NonBurstCache.MarkNonBurst(file, info);
                    skipped++;
                    continue;
                }

                string parent = Path.GetDirectoryName(Path.GetFullPath(file))!;
                string outDir = Path.Combine(parent, Path.GetFileNameWithoutExtension(file));

                Console.WriteLine($"BURST ({frames} frames): {file}");
                Console.WriteLine($"  -> {outDir}");

                burstsFound++;
                progress.Report((processed, total, burstsFound, framesExtracted));

                int written = BurstExtractor.Extract(file, outDir);
                framesExtracted += written;

                string destBackup = UniquePath(Path.Combine(backupDir, Path.GetFileName(file)));
                File.Move(file, destBackup);
                Console.WriteLine($"  moved original -> {destBackup}");
                Console.WriteLine();

                extracted++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR processing {file}: {ex.Message}");
                errors++;
            }
            finally
            {
                processed++;
                progress.Report((processed, total, burstsFound, framesExtracted));
            }
        }

        NonBurstCache.Save();

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

    static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    void SetBusy(bool busy)
    {
        _extractButton.Text = busy ? "Stop" : "Extract";
        _extractButton.Enabled = true;
        _scanBrowse.Enabled = !busy;
        _backupBrowse.Enabled = !busy;
        _scanDirBox.Enabled = !busy;
        _backupDirBox.Enabled = !busy;
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
