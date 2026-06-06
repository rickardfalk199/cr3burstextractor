using System;
using System.Windows.Forms;

namespace Cr3BurstExtractor;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!UserSettings.SkipSplash)
        {
            using var splash = new SplashForm();
            if (splash.ShowDialog() != DialogResult.OK) return;
        }

        Application.Run(new MainForm());
    }
}