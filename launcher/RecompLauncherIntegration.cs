using System;
using System.IO;
using System.Text;

internal static class RecompLauncherIntegration
{
    internal static int Run(string[] args)
    {
        // The Windows GUI executable can have pipes without an attached console.
        var utf8 = new UTF8Encoding(false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        string log = null;
        try
        {
            bool prepare = args[0] == "--prepare";
            string installation = null, disc = null;
            bool wide = true;
            bool fullscreen = Environment.GetEnvironmentVariable("RECOMPLAUNCHER_MODE") == "bigpicture";
            bool? skipIntros = null;
            for (int i = 1; i < args.Length; ++i)
            {
                string arg = args[i];
                if (arg.StartsWith("--install=", StringComparison.Ordinal))
                    installation = arg.Substring("--install=".Length);
                else if (prepare && arg.StartsWith("--game=", StringComparison.Ordinal))
                    disc = arg.Substring("--game=".Length);
                else if (!prepare && arg == "--aspect=4:3") wide = false;
                else if (!prepare && arg == "--aspect=16:9") wide = true;
                else if (!prepare && arg == "--windowed") fullscreen = false;
                else if (!prepare && arg == "--fullscreen") fullscreen = true;
                else if (!prepare && arg == "--skip-intros") skipIntros = true;
                else if (!prepare && arg == "--play-intros") skipIntros = false;
                else throw new ArgumentException("Unknown ReSqueezed option: " + arg);
            }
            if (String.IsNullOrWhiteSpace(installation))
                throw new ArgumentException("Pass the ReSqueezed install folder with --install=.");
            installation = Path.GetFullPath(installation).TrimEnd(Path.DirectorySeparatorChar);
            if (!prepare)
                return Builder.RunGame(installation, wide, fullscreen, skipIntros, managed: true);

            string data = Path.Combine(installation, "recompdata");
            Directory.CreateDirectory(data);
            log = Builder.NewLog(installation);
            string ready = Path.Combine(data, "resqueezed.prepared");
            using (File.Open(Path.Combine(data, "resqueezed-setup.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None))
            {
                File.Delete(ready);
                WriteProgress("0", "Checking the game installation");
                Builder.Prepare(disc, installation, log, BuildProgress);
                File.WriteAllText(ready, Builder.Release);
            }
            WriteProgress("1", "ReSqueezed is ready to play");
            return 0;
        }
        catch (Exception ex)
        {
            if (log != null)
            {
                File.AppendAllText(log, ex + Environment.NewLine);
                Console.Error.WriteLine("Build log " + log);
            }
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void BuildProgress(string message)
    {
        string fraction = message.StartsWith("Generating", StringComparison.Ordinal) ? "0.35" :
            message.StartsWith("Configuring", StringComparison.Ordinal) ? "0.65" :
            message.StartsWith("Compiling", StringComparison.Ordinal) ? "0.72" :
            message.StartsWith("Downloading", StringComparison.Ordinal) ? "0.10" :
            message.StartsWith("Extracting", StringComparison.Ordinal) ||
            message.StartsWith("Copying", StringComparison.Ordinal) ||
            message.StartsWith("Using", StringComparison.Ordinal) ? "0.25" :
            message.IndexOf("ready to play", StringComparison.Ordinal) >= 0 ? "0.98" : "0.05";
        WriteProgress(fraction, message);
    }

    private static void WriteProgress(string fraction, string message)
    {
        Console.WriteLine("progress " + fraction + " " + message);
    }
}
