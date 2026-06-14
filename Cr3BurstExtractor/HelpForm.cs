using System.Drawing;
using System.Windows.Forms;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor;

public sealed class HelpForm : Form
{
    public HelpForm()
    {
        Text = "Help — " + AppInfo.Name;
        Icon = LogoRenderer.CreateIcon(64);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(680, 580);
        MinimumSize = new Size(520, 400);
        BackColor = Color.White;

        var titleLabel = new Label
        {
            Text = AppInfo.Name,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            Left = 24, Top = 18,
            Width = ClientSize.Width - 48, Height = 32,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var subtitleLabel = new Label
        {
            Text = "Help",
            Font = new Font("Segoe UI", 11f),
            ForeColor = Color.DimGray,
            Left = 24, Top = 52,
            Width = ClientSize.Width - 48, Height = 22,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var content = new FlowLayoutPanel
        {
            Left = 24, Top = 84,
            Width = ClientSize.Width - 48,
            Height = ClientSize.Height - 84 - 64,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.White
        };

        int textWidth = content.Width - 28; // reserve room for the vertical scrollbar

        AddSection(
            content,
            textWidth,
            "What it does",
            "CR3 Burst Extractor pulls individual frames out of Canon RAW Burst roll files " +
            "(typically named CSI_*.CR3) and writes each frame as a standalone .CR3 that can be " +
            "opened in Canon DPP, Adobe Lightroom, darktable, etc.");

        AddBullets(
            content,
            textWidth,
            "How to use",
            "Pick a Scan folder. The application searches it recursively for .CR3 files.",
            "Decide what to do with the original burst file after extraction:" +
            "  •  Tick \"Move originals to backup folder\" and pick a backup folder — each burst will be moved there after its frames are written." +
            "  •  Leave it unchecked to keep the burst file in its original directory next to the new sub-folder of frames.",
            "Click Extract.");

        AddSection(
            content,
            textWidth,
            null,
            "You can press Stop while a run is in progress. The current file is always finished " +
            "first, then the run halts before the next one.");

        AddBullets(
            content,
            textWidth,
            "What happens to each .CR3",
            "More than one frame (a burst roll): each frame is written into a new sub-folder placed " +
            "next to the original file, named after the original file (without extension). After " +
            "the frames are written, the original burst file is either moved to the Backup folder " +
            "(if \"Move originals to backup folder\" is ticked) or left in its original directory.",
            "Only one frame: skipped — not a burst — the file is left untouched.",
            "Unreadable or invalid: logged as an error; the scan continues with the next file.");

        AddSection(
            content,
            textWidth,
            "Background service (auto-extract)",
            "The same executable can also run as a Windows Service that watches the Scan folder and " +
            "auto-extracts new bursts as they appear — e.g. straight off a card reader or camera " +
            "offload. The service applies the same per-file logic as the Extract button: cache check, " +
            "frame count, extract (or mark as single-frame), and optional move to the Backup folder.");

        AddBullets(
            content,
            textWidth,
            "Setting it up",
            "Set the Scan folder (and Backup folder, if you want originals moved).",
            "Service → Install service…  (prompts for UAC).",
            "Settings → Auto-extract new files in scan folder  — tick it.",
            "Service → Start service.");

        AddSection(
            content,
            textWidth,
            null,
            "The service runs as LocalSystem and starts automatically with Windows. " +
            "Toggling the auto-extract setting takes effect immediately — no service restart needed. " +
            "Use Service → Stop / Start / Uninstall to manage it from the form, or " +
            "Cr3BurstExtractor.exe --install / --start / --stop / --status / --uninstall from a terminal.");

        AddBullets(
            content,
            textWidth,
            "Windows notifications",
            "When the service successfully extracts a burst, it pops a Windows toast " +
            "(\"Extracted N frames from xyz.CR3\") in the logged-in user's session.",
            "Toggle this off via Settings → Show Windows notification on auto-extract if it gets noisy " +
            "during long camera offloads.",
            "Toasts only reach the active console session — if nobody is logged in, or only an RDP " +
            "user is connected, the extraction still happens but the toast is skipped.");

        AddBullets(
            content,
            textWidth,
            "Logs and config",
            "Settings live in %ProgramData%\\Cr3BurstExtractor\\settings.json (shared between the form, " +
            "the CLI, and the service).",
            "Non-burst cache lives in %ProgramData%\\Cr3BurstExtractor\\non_burst_cache.json.",
            "Service activity is logged to %ProgramData%\\Cr3BurstExtractor\\service.log (rolls at 5 MB) " +
            "and to the Windows Event Log under source Cr3BurstExtractor.");

        AddBullets(
            content,
            textWidth,
            "Notes",
            "When moving originals is enabled, files already inside the Backup folder are ignored " +
            "by the scan, so re-runs don't re-process previously archived bursts.",
            "When leaving originals in place, a second scan over the same folder will re-detect each " +
            "burst as a burst again — but the extracted single-frame .CR3 files in their sub-folders " +
            "are cached as non-burst, so they will not be re-opened.",
            "Extracted single-frame .CR3 files are fully valid standalone files; opening them in " +
            "your RAW developer should just work.",
            "All progress and per-file logging appears in the box at the bottom of the main window.");

        var close = new Button
        {
            Text = "Close",
            Width = 100,
            Height = 32,
            Left = ClientSize.Width - 112,
            Top = ClientSize.Height - 44,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        AcceptButton = close;
        CancelButton = close;

        Controls.AddRange(new Control[] { titleLabel, subtitleLabel, content, close });
    }

    static void AddSection(FlowLayoutPanel parent, int textWidth, string? header, string body)
    {
        if (header != null)
        {
            parent.Controls.Add(
                new Label
                {
                    Text = header,
                    Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(30, 110, 200),
                    AutoSize = true,
                    Margin = new Padding(0, 14, 0, 6)
                });
        }

        parent.Controls.Add(
            new Label
            {
                Text = body,
                Font = new Font("Segoe UI", 10f),
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Margin = new Padding(0, 0, 0, 8)
            });
    }

    static void AddBullets(FlowLayoutPanel parent, int textWidth, string header, params string[] items)
    {
        parent.Controls.Add(
            new Label
            {
                Text = header,
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 110, 200),
                AutoSize = true,
                Margin = new Padding(0, 14, 0, 6)
            });
        foreach (var item in items)
        {
            parent.Controls.Add(
                new Label
                {
                    Text = "•  " + item,
                    Font = new Font("Segoe UI", 10f),
                    AutoSize = true,
                    MaximumSize = new Size(textWidth, 0),
                    Margin = new Padding(0, 0, 0, 4)
                });
        }
    }
}