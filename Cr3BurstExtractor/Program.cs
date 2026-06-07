using System;
using System.Windows.Forms;

namespace Cr3BurstExtractor;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // One-shot icon generator: produces a multi-resolution app.ico from the
        // burst-stack logo. Used at build setup time so the published .exe gets
        // a custom icon in Explorer.
        if (args.Length > 0 && args[0] == "--generate-icon")
        {
            string outPath = args.Length > 1 ? args[1] : "app.ico";
            IconExporter.WriteMultiResIcon(outPath, new[] { 16, 32, 48, 64, 128, 256 });
            return;
        }

        ApplicationConfiguration.Initialize();

        if (!UserSettings.SkipSplash)
        {
            using var splash = new SplashForm();
            if (splash.ShowDialog() != DialogResult.OK) return;
        }

        Application.Run(new MainForm());
    }
}
