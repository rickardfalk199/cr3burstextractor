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