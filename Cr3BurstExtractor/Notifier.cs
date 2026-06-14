using System.Windows.Forms;
using Cr3BurstExtractor.Helpers;

namespace Cr3BurstExtractor;

/// <summary>
/// Handler for the <c>--notify "&lt;message&gt;"</c> subcommand. Pops a balloon
/// tip via <see cref="NotifyIcon"/> and exits a few seconds later. On Win10/11
/// this routes through the Action Center toast UI; on older Windows it's the
/// classic system-tray balloon.
///
/// The service can't show notifications itself (session 0 isolation) — it
/// uses <see cref="SessionLauncher.LaunchInActiveSession"/> to spawn this
/// exe with <c>--notify</c> in the active user's session.
/// </summary>
public static class Notifier
{
    public static int Run(string message)
    {
        ApplicationConfiguration.Initialize();

        using var icon = new NotifyIcon
        {
            Icon = LogoRenderer.CreateIcon(64),
            Visible = true,
            BalloonTipTitle = AppInfo.Name,
            BalloonTipText = message,
            BalloonTipIcon = ToolTipIcon.Info,
        };
        icon.ShowBalloonTip(5000);

        // Hold a real message pump long enough for the shell to render the
        // toast and for the user to see it before we tear down the icon.
        // A bare Thread.Sleep won't suffice — NotifyIcon needs the pump.
        using var timer = new System.Windows.Forms.Timer { Interval = 6000 };
        timer.Tick += (_, _) =>
        {
            icon.Visible = false;
            Application.Exit();
        };
        timer.Start();
        Application.Run();
        return 0;
    }
}
