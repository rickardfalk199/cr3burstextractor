using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor;

public sealed class SplashForm : Form
{
    readonly CheckBox? _dontShowAgain;

    public SplashForm(bool isAbout = false)
    {
        Text = (isAbout ? "About — " : "") + AppInfo.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = isAbout ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = !isAbout;
        ClientSize = new Size(520, 510);
        BackColor = Color.White;
        Icon = LogoRenderer.CreateIcon(64);

        var logo = new LogoPanel
        {
            Left = (ClientSize.Width - 140) / 2,
            Top = 18,
            Width = 140,
            Height = 120
        };

        var title = new Label
        {
            Text = AppInfo.Name,
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Left = 0, Top = 145, Width = ClientSize.Width, Height = 38
        };

        var version = new Label
        {
            Text = "Version " + AppInfo.Version,
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Left = 0, Top = 183, Width = ClientSize.Width, Height = 18
        };

        var subtitle = new Label
        {
            Text = AppInfo.Tagline,
            Font = new Font("Segoe UI", 10f),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Left = 0, Top = 208, Width = ClientSize.Width, Height = 22
        };

        var disclaimerTitle = new Label
        {
            Text = "DISCLAIMER",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Firebrick,
            Left = 0, Top = 240, Width = ClientSize.Width, Height = 22
        };

        var disclaimer = new Label
        {
            Text = "This software is provided \"as is\", without warranty of any kind. " +
                   "The author takes no responsibility for any data loss, file corruption, " +
                   "or other damages resulting from its use. Use at your own risk.",
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleCenter,
            Left = 40, Top = 265, Width = ClientSize.Width - 80, Height = 70
        };

        const string separator = "  ·  ";
        string contactText = AppInfo.AuthorName + separator + AppInfo.AuthorEmail;

        var contact = new LinkLabel
        {
            Text = contactText,
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleCenter,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Left = 0, Top = 345, Width = ClientSize.Width, Height = 22
        };
        contact.Links.Add(
            AppInfo.AuthorName.Length + separator.Length,
            AppInfo.AuthorEmail.Length,
            "mailto:" + AppInfo.AuthorEmail);
        contact.LinkClicked += (_, e) => OpenLink(e);

        var repo = new LinkLabel
        {
            Text = AppInfo.RepoDisplay,
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleCenter,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Left = 0, Top = 370, Width = ClientSize.Width, Height = 22
        };
        repo.Links.Add(0, AppInfo.RepoDisplay.Length, AppInfo.RepoUrl);
        repo.LinkClicked += (_, e) => OpenLink(e);

        var ok = new Button
        {
            Text = isAbout ? "Close" : "I understand – continue",
            Width = 200,
            Height = 34,
            Left = (ClientSize.Width - 200) / 2,
            Top = 450,
            DialogResult = DialogResult.OK
        };
        AcceptButton = ok;

        var controls = new System.Collections.Generic.List<Control>
        {
            logo, title, version, subtitle, disclaimerTitle, disclaimer, contact, repo, ok
        };

        if (!isAbout)
        {
            _dontShowAgain = new CheckBox
            {
                Text = "Don't show this screen again",
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Top = 412,
                Checked = UserSettings.SkipSplash
            };
            _dontShowAgain.Left = (ClientSize.Width - _dontShowAgain.PreferredSize.Width) / 2;
            controls.Add(_dontShowAgain);

            FormClosed += (_, _) =>
            {
                if (DialogResult == DialogResult.OK)
                {
                    UserSettings.SkipSplash = _dontShowAgain.Checked;
                    UserSettings.Save();
                }
            };
        }

        Controls.AddRange(controls.ToArray());
    }

    static void OpenLink(LinkLabelLinkClickedEventArgs e)
    {
        if (e.Link?.LinkData is string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                /* no default handler registered — silently ignore */
            }
        }
    }

    sealed class LogoPanel : Panel
    {
        public LogoPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            LogoRenderer.Draw(e.Graphics, Width, Height);
        }
    }
}