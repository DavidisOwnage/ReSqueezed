using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal static class Builder
{
    internal static readonly string Package = AppDomain.CurrentDomain.BaseDirectory;
    internal const string GameExe = "ReSqueezed.exe";
    internal const string Release = "0.2a";
    private static readonly Version GameVersion = new Version(0, 2, 0, 1);

    internal static string InstalledExecutable(string folder)
    {
        string current = Path.Combine(folder, GameExe);
        return File.Exists(current) ? current : Path.Combine(folder, "JuicedRecomp.exe");
    }

    internal static string InstalledBuildLog(string folder)
    {
        string current = Path.Combine(folder, "resqueezed-build.log");
        return File.Exists(current) ? current : Path.Combine(folder, "juiced-build.log");
    }

    internal static string NewLog(string installation = null)
    {
        string directory = installation == null ? Path.Combine(Path.GetTempPath(), "ReSqueezed", "Logs")
            : Path.Combine(installation, "logs");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "build-" + Guid.NewGuid().ToString("N") + ".log");
    }

    internal static bool Installed(string path)
    {
        if (String.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return false;
        return File.Exists(InstalledExecutable(path)) &&
               File.Exists(Path.Combine(path, "game", "default.xbe"));
    }

    internal static bool UpdateAvailable(string folder)
    {
        return Installed(folder) && ReadVersion(InstalledExecutable(folder)) < GameVersion;
    }

    private static Version ReadVersion(string executable)
    {
        var info = FileVersionInfo.GetVersionInfo(executable);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    internal static void Update(string destination, string log, Action<string> status)
    {
        if (!Installed(destination))
            throw new InvalidOperationException("Choose an existing ReSqueezed installation.");
        if (!UpdateAvailable(destination))
        {
            status("This installation is already up to date.");
            return;
        }
        Build(Path.Combine(destination, "game"), destination, log, status, true);
    }

    internal static void Prepare(string input, string destination, string log, Action<string> status)
    {
        string game = Path.Combine(destination, "game");
        if (Directory.Exists(game))
        {
            status("Verifying the retained disc files…");
            DiscValidator.ValidateFolder(game);
            if (!String.IsNullOrEmpty(input) && !String.Equals(Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar),
                game, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(input)) DiscValidator.ValidateFolder(input);
                else DiscValidator.ValidateIso(input);
            }
            string target = Path.Combine(destination, GameExe);
            if (File.Exists(target) && ReadVersion(target) == GameVersion && File.Exists(Path.Combine(destination, "SDL3.dll")))
                return;
            Build(game, destination, log, status, update: true, managed: true);
            return;
        }
        if (String.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("Select your USA / Europe Xbox ISO or extracted disc folder in Easy Setup.");
        Build(input, destination, log, status, update: File.Exists(InstalledExecutable(destination)), managed: true);
    }

    internal static void Build(string input, string destination, string log, Action<string> status,
        bool update = false, bool managed = false)
    {
        if (String.IsNullOrWhiteSpace(input) || String.IsNullOrWhiteSpace(destination))
            throw new InvalidOperationException("Choose an Xbox ISO or game folder and an install folder.");

        input = Path.GetFullPath(input);
        destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        string parent = Path.GetDirectoryName(destination);
        if (String.IsNullOrEmpty(parent))
            throw new InvalidOperationException("Choose an install folder below the drive root.");
        string installedGame = Path.Combine(destination, "game");
        bool reuseGame = String.Equals(input.TrimEnd(Path.DirectorySeparatorChar), installedGame,
            StringComparison.OrdinalIgnoreCase);
        string[] installedFiles = { GameExe, "SDL3.dll", "SDL3-LICENSE.txt", "Play ReSqueezed.exe", "resqueezed-build.log" };
        if (!update)
            foreach (string file in installedFiles)
                if (File.Exists(Path.Combine(destination, file)) || Directory.Exists(Path.Combine(destination, file)))
                    throw new InvalidOperationException("The install folder already contains " + file + ". Use Update or choose another folder.");
        if (!reuseGame && (Directory.Exists(installedGame) || File.Exists(installedGame)))
            throw new InvalidOperationException("A game folder already exists here. Select that folder as disc input or choose another install folder.");

        bool extract = File.Exists(input) && Path.GetExtension(input).Equals(".iso", StringComparison.OrdinalIgnoreCase);
        if (!extract && !Directory.Exists(input))
            throw new InvalidOperationException("Choose an Xbox ISO or the extracted folder containing default.xbe.");
        status("Verifying the retail disc…");
        if (extract) DiscValidator.ValidateIso(input);
        if (!extract)
        {
            DiscValidator.ValidateFolder(input);
            string gameRoot = input.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((destination + Path.DirectorySeparatorChar).StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose an install folder outside the extracted disc folder.");
        }

        string source = Path.Combine(Package, "source", "ReSqueezed");
        string runtime = Path.Combine(Package, "source", "xboxrecomp");
        if (!File.Exists(Path.Combine(source, "CMakeLists.txt")) ||
            !File.Exists(Path.Combine(runtime, "CMakeLists.txt")))
            throw new InvalidOperationException("Extract the complete builder ZIP so the source folder is beside the launcher.");
        Dependencies.Ensure(status);
        string python = BundledTool(@"python\python.exe");
        string extractor = extract ? BundledTool(@"extract-xiso\extract-xiso.exe") : null;
        string compiler = BundledTool(@"llvm-mingw-ucrt\bin\x86_64-w64-mingw32-clang.exe");
        string cmake = BundledTool(@"cmake\bin\cmake.exe");
        string ninja = BundledTool(@"ninja\ninja.exe");

        Directory.CreateDirectory(parent);
        string workspace = Path.Combine(managed ? destination : parent, ".resqueezed-build-" + Guid.NewGuid().ToString("N"));
        string install = Path.Combine(workspace, "install");
        string game = Path.Combine(install, "game");
        string objects = Path.Combine(workspace, "obj");
        string project = Path.Combine(workspace, "project");
        Directory.CreateDirectory(install);
        try
        {
            string[] paths = { Path.GetDirectoryName(compiler), Path.GetDirectoryName(ninja) };
            if (reuseGame)
            {
                status("Using the selected game folder in place…");
            }
            else if (extract)
            {
                status("Extracting Xbox ISO…");
                Directory.CreateDirectory(game);
                Run(extractor, workspace, log, paths, "-x", "-d", game, input);
            }
            else
            {
                status("Copying the extracted disc…");
                Directory.CreateDirectory(game);
                CopyDisc(input, game);
            }
            DiscValidator.ValidateFolder(reuseGame ? installedGame : game);

            status("Generating code from your disc…");
            CopyProject(source, project);
            Run(python, workspace, log, paths, Path.Combine(project, "build-support", "generate.py"),
                "--game", reuseGame ? installedGame : game, "--project", project,
                "--pipeline", Path.Combine(Package, "source", "pipeline"),
                "--scratch", Path.Combine(workspace, "analysis"));
            status("Configuring the Windows x64 build…");
            Run(cmake, workspace, log, paths, "-S", project, "-B", objects, "-G", "Ninja",
                "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_C_COMPILER=" + compiler,
                "-DCMAKE_MAKE_PROGRAM=" + ninja, "-DCMAKE_EXE_LINKER_FLAGS=-static",
                "-DXBOXRECOMP_DIR=" + runtime, "-DJUICED_SDL3_DIR=" + Path.Combine(Package, "tools", "sdl3"), "-DXBOXRECOMP_WIN_VULKAN=OFF",
                "-DXBOXRECOMP_ENHANCE=OFF", "-DXBOXRECOMP_OVERLAY=OFF");
            status("Compiling ReSqueezed…");
            Run(cmake, workspace, log, paths, "--build", objects, "--target", "ReSqueezed",
                "--parallel", Math.Min(Environment.ProcessorCount, 6).ToString());
            string compiledGame = Path.Combine(objects, GameExe);
            if (ReadVersion(compiledGame) != GameVersion)
                throw new InvalidOperationException("The build produced an unexpected version of ReSqueezed.exe.");
            if (update)
            {
                using (new CareerSaves(destination).Lock())
                {
                    // Another launcher may have finished while this one was compiling.
                    if (managed || UpdateAvailable(destination))
                    {
                        if (managed && !File.Exists(Path.Combine(destination, "SDL3.dll")))
                        {
                            string license = Path.Combine(destination, "SDL3-LICENSE.txt");
                            if (!File.Exists(license))
                                File.Copy(Path.Combine(Package, "tools", "sdl3", "share", "licenses", "SDL3", "LICENSE.txt"), license);
                            File.Move(Path.Combine(objects, "SDL3.dll"), Path.Combine(destination, "SDL3.dll"));
                        }
                        string target = Path.Combine(destination, GameExe);
                        if (!reuseGame) Directory.Move(game, installedGame);
                        try
                        {
                            if (File.Exists(target)) File.Replace(compiledGame, target, null);
                            else File.Move(compiledGame, target);
                        }
                        catch
                        {
                            if (!reuseGame) Directory.Move(installedGame, game);
                            throw;
                        }
                    }
                }
                status("ReSqueezed v" + Release + " is ready to play.");
                return;
            }
            File.Copy(compiledGame, Path.Combine(install, GameExe));
            File.Copy(Path.Combine(objects, "SDL3.dll"), Path.Combine(install, "SDL3.dll"));
            File.Copy(Path.Combine(Package, "tools", "sdl3", "share", "licenses", "SDL3", "LICENSE.txt"), Path.Combine(install, "SDL3-LICENSE.txt"));
            File.Copy(ApplicationPath(), Path.Combine(install, "Play ReSqueezed.exe"));
            File.Copy(log, Path.Combine(install, "resqueezed-build.log"));
            PublishInstall(install, destination, reuseGame);
            status("Build complete. ReSqueezed is ready to play.");
        }
        finally
        {
            try { DeleteBuildTree(workspace); }
            catch (IOException ex) { File.AppendAllText(log, Environment.NewLine + ex.Message); }
            catch (UnauthorizedAccessException ex) { File.AppendAllText(log, Environment.NewLine + ex.Message); }
        }
    }

    private static void PublishInstall(string staged, string destination, bool reuseGame)
    {
        Directory.CreateDirectory(destination);
        // Publish the game executable last. The Play action only becomes
        // available when all the other install files have been moved.
        var moved = new Stack<string>();
        try
        {
            if (!reuseGame)
            {
                Directory.Move(Path.Combine(staged, "game"), Path.Combine(destination, "game"));
                moved.Push("game");
            }
            foreach (string file in new[] { "SDL3.dll", "SDL3-LICENSE.txt", "Play ReSqueezed.exe", "resqueezed-build.log", GameExe })
            {
                File.Move(Path.Combine(staged, file), Path.Combine(destination, file));
                moved.Push(file);
            }
        }
        catch
        {
            foreach (string item in moved)
            {
                string source = Path.Combine(destination, item);
                string target = Path.Combine(staged, item);
                if (item == "game") Directory.Move(source, target);
                else File.Move(source, target);
            }
            throw;
        }
    }

    private static void CopyProject(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Build sources contain a filesystem link.");
            string relative = entry.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
            if (relative == "gen" && Path.GetFileName(source) == "recomp") continue;
            string target = Path.Combine(destination, relative);
            if (Directory.Exists(entry)) CopyProject(entry, target);
            else File.Copy(entry, target);
        }
    }

    private static void CopyDisc(string source, string destination)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The extracted folder contains a link. Use a regular disc extraction.");
            string target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(target);
                CopyDisc(entry, target);
            }
            else
            {
                File.Copy(entry, target);
                File.SetAttributes(target, FileAttributes.Normal);
            }
        }
    }

    private static void DeleteBuildTree(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (Directory.Exists(entry))
                    DeleteBuildTree(entry);
                else
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                    File.Delete(entry);
                }
            }
        }
        Directory.Delete(directory);
    }

    private static string BundledTool(string relative)
    {
        string path = Path.Combine(Package, "tools", relative);
        if (File.Exists(path)) return path;
        throw new InvalidOperationException("The package is missing " + Path.GetFileName(path) + ". Extract the complete builder ZIP.");
    }

    private static string Quote(string value)
    {
        // Windows argv treats a run of backslashes before a quote specially.
        return "\"" + System.Text.RegularExpressions.Regex.Replace(value, "(\\\\*)(\"|$)",
            match => match.Groups[1].Value + match.Groups[1].Value +
                     (match.Groups[2].Value == "\"" ? "\\\"" : "")) + "\"";
    }

    private static ProcessStartInfo StartInfo(string exe, string directory, params string[] arguments)
    {
        return new ProcessStartInfo(exe, String.Join(" ", arguments.Select(Quote))) {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true
        };
    }

    private static void Run(string exe, string directory, string log, string[] paths, params string[] arguments)
    {
        var start = StartInfo(exe, directory, arguments);
        start.EnvironmentVariables["PATH"] = String.Join(";", paths) + ";" + Environment.SystemDirectory;
        int result = RunLogged(start, log);
        if (result != 0)
            throw new InvalidOperationException(Path.GetFileName(exe) + " exited with code " + result + ". Open the build log for details.");
    }

    private static int RunLogged(ProcessStartInfo start, string log)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using (var writer = new StreamWriter(log, true, Encoding.UTF8))
        using (var process = new Process { StartInfo = start })
        {
            writer.AutoFlush = true;
            writer.WriteLine(start.FileName + " " + start.Arguments);
            DataReceivedEventHandler write = (sender, data) => {
                if (data.Data != null) lock (writer) writer.WriteLine(data.Data);
            };
            process.OutputDataReceived += write;
            process.ErrorDataReceived += write;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    internal static string ApplicationPath() { return System.Windows.Forms.Application.ExecutablePath; }

    internal static void Play(string installation, bool wide)
    {
        if (!Installed(installation))
            throw new InvalidOperationException("Choose a completed ReSqueezed installation.");
        installation = Path.GetFullPath(installation);
        using (Process.Start(StartInfo(ApplicationPath(), installation, "--run-game", installation, wide ? "wide" : "4x3"))) { }
    }

    internal static bool FasterStartup(string installation)
    {
        return Installed(installation) && File.Exists(Path.Combine(installation, "skip-startup-movies.enabled"));
    }

    internal static void SetFasterStartup(string installation, bool enabled)
    {
        if (!Installed(installation)) throw new InvalidOperationException("Choose a completed ReSqueezed installation.");
        string setting = Path.Combine(installation, "skip-startup-movies.enabled");
        if (enabled)
        {
            using (File.Open(setting, FileMode.OpenOrCreate)) { }
        }
        else File.Delete(setting);
    }

    private static void TryAutoBackup(CareerSaves saves, string log)
    {
        try { saves.BackupAll(); }
        catch (IOException ex) { File.AppendAllText(log, "Career backup failed. " + ex.Message + Environment.NewLine); }
        catch (UnauthorizedAccessException ex) { File.AppendAllText(log, "Career backup failed. " + ex.Message + Environment.NewLine); }
    }

    internal static int RunGame(string installation, bool wide, bool fullscreen = true,
        bool? skipIntros = null, bool managed = false)
    {
        if (!Installed(installation))
            throw new InvalidOperationException("The installation is missing ReSqueezed.exe or game/default.xbe.");
        installation = Path.GetFullPath(installation);
        string logs = Path.Combine(installation, "logs");
        Directory.CreateDirectory(logs);
        var start = StartInfo(InstalledExecutable(installation), installation);
        foreach (string key in start.EnvironmentVariables.Keys.Cast<string>()
            .Where(key => key.StartsWith("RECOMP_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.EnvironmentVariables.Remove(key);
        var settings = new Dictionary<string, string> {
            { "AC97_READY", "1" }, { "PB_EXEC", "1" }, { "PB_WALK", "dma" },
            { "VBLANK", "paced" }, { "TITLE_KEVENTS", "1" },
            { "USB", "1" }, { "USB_PORT", "0" }, { "KEYBOARD", "1" },
            { "PB_BACKEND", "d3d11" }, { "APU_DSP_ACK", "auto" },
            { "AUDIO_BUF_SAMPLES", "1024" }, { "AUDIO_BUF_COUNT", "8" },
            { "JUICED_AUDIO_SE_PAUSE", "1" }, { "JUICED_SCANOUT", "1" },
            { "SAVE_DIR", Path.Combine(installation, "saves") },
            { "RENDER_SCALE", "2" }, { "PRESENT_FULLSCREEN", fullscreen ? "1" : "0" },
            { "PRESENT_FILTER", "linear" }, { "GUEST_CPUS", "all" }, { "GFX_ANISO", "8" },
            { "D3D11_NV2A_RASTER", "1" }, { "JUICED_REVERSE_DEPTH", "1" },
            { "JUICED_W_DEPTH", "1" }, { "JUICED_EVENT_KICK", "1" },
            { "DISPLAY_WIDESCREEN", wide ? "1" : "0" },
            { "JUICED_NATIVE_WIDE", wide ? "1" : "0" },
            { "JUICED_SKIP_INTROS", (skipIntros ?? FasterStartup(installation)) ? "1" : "0" },
            { "PRESENT_ASPECT", wide ? "16:9" : "4:3" }, { "JUICED_FOV", "0" }
        };
        foreach (var setting in settings)
            start.EnvironmentVariables["RECOMP_" + setting.Key] = setting.Value;
        start.EnvironmentVariables.Remove("RESQUEEZED_MANAGED_PLAY");
        if (managed) start.EnvironmentVariables["RESQUEEZED_MANAGED_PLAY"] = "1";
        string log = Path.Combine(logs, "play-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
        var saves = new CareerSaves(installation);
        int exit;
        using (saves.Lock())
        {
            TryAutoBackup(saves, log);
            exit = RunLogged(start, log);
            if (exit == 0) TryAutoBackup(saves, log);
        }
        if (exit != 0) throw new InvalidOperationException("ReSqueezed exited with code " + exit + ". See " + log);
        return exit;
    }
}
