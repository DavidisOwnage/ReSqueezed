using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class CareerSaves
{
    private const string TitleId = "545100ef";
    private const string DirectoryPrefix = "UDATA/" + TitleId + "/";
    private const string CareerFile = "juicey.dat";
    private readonly string installation;
    private readonly string profiles;
    private readonly string backups;

    internal CareerSaves(string installation)
    {
        this.installation = Path.GetFullPath(installation);
        profiles = Path.Combine(this.installation, "saves", "UDATA", TitleId);
        backups = Path.Combine(this.installation, "save-backups");
    }

    internal FileStream Lock()
    {
        FileStream session;
        try
        {
            session = new FileStream(Path.Combine(installation, ".juiced-save.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            if ((ex.HResult & 0xffff) == 32)
                throw new InvalidOperationException("Close ReSqueezed before managing career saves.");
            throw;
        }
        try
        {
            // Existing installs can still be launched by an older shortcut.
            string game = Path.Combine(installation, Builder.GameExe);
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Builder.GameExe)))
            using (process)
            {
                string running;
                try { running = process.MainModule.FileName; }
                catch (System.ComponentModel.Win32Exception) { continue; }
                catch (InvalidOperationException) { continue; }
                if (String.Equals(running, game, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Close ReSqueezed before managing career saves.");
            }
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal sealed class Profile
    {
        internal string Id;
        internal string Name;
        public override string ToString() { return Name + "  (" + Id + ")"; }
    }

    internal sealed class Backup
    {
        internal string Path;
        internal DateTime Created;
        internal string Kind;
        public override string ToString() { return Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "  •  " + Kind; }
    }

    private static bool IsId(string value)
    {
        return Regex.IsMatch(value, @"\A[0-9a-fA-F]{12}\z");
    }

    private static string ReadName(string directory, string fallback)
    {
        string metadata = Path.Combine(directory, "SaveMeta.xbx");
        if (File.Exists(metadata))
        {
            foreach (string line in File.ReadLines(metadata, Encoding.Unicode))
            {
                if (line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                {
                    string name = line.Substring(5).TrimEnd('\0').Trim();
                    if (name.Length != 0) return name;
                }
            }
        }
        return fallback;
    }

    internal Profile[] ListProfiles()
    {
        var result = new List<Profile>();
        if (Directory.Exists(profiles))
        {
            foreach (string directory in Directory.EnumerateDirectories(profiles))
            {
                string id = Path.GetFileName(directory);
                if (IsId(id) && File.Exists(Path.Combine(directory, CareerFile)))
                    result.Add(new Profile { Id = id, Name = ReadName(directory, id) });
            }
        }
        // Keep backups available when a driver was deleted in-game.
        if (Directory.Exists(backups))
        {
            foreach (string directory in Directory.EnumerateDirectories(backups))
            {
                string id = Path.GetFileName(directory);
                if (IsId(id) && !result.Any(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    && ListBackups(id).Length != 0)
                    result.Add(new Profile { Id = id, Name = "Backed-up driver" });
            }
        }
        return result.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal bool HasCareer(string id)
    {
        return File.Exists(Path.Combine(profiles, id, CareerFile));
    }

    internal Backup[] ListBackups(string id)
    {
        var directory = new DirectoryInfo(Path.Combine(backups, id));
        if (!directory.Exists) return new Backup[0];
        return directory.EnumerateFiles("*.zip").OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Select(file => new Backup {
                Path = file.FullName,
                Created = file.LastWriteTimeUtc,
                Kind = file.Name.Contains("-auto-") ? "Automatic" :
                    file.Name.Contains("-before-restore-") ? "Before restore" : "Manual"
            }).ToArray();
    }

    // The caller holds the save lock, including while a launched game runs.
    internal void BackupAll()
    {
        foreach (var profile in ListProfiles())
        {
            string source = Path.Combine(profiles, profile.Id);
            if (!HasCareer(profile.Id)) continue;
            var previous = ListBackups(profile.Id);
            if (previous.Length > 0 && Matches(source, previous[0].Path)) continue;
            CreateBackup(profile.Id, "auto");
            foreach (var backup in ListBackups(profile.Id).Where(item => item.Kind == "Automatic").Skip(5))
                File.Delete(backup.Path);
        }
    }

    internal void BackupNow(string id)
    {
        using (Lock()) CreateBackup(id, "manual");
    }

    internal void Export(string id, string target)
    {
        string fullPath = Path.GetFullPath(target);
        if (!Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a .zip filename for the exported save.");
        if (fullPath.StartsWith(profiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(backups + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export the ZIP outside the local save and backup folders.");
        using (Lock()) WriteArchive(id, target, true);
    }

    private void CreateBackup(string id, string kind)
    {
        string folder = Path.Combine(backups, id);
        Directory.CreateDirectory(folder);
        string filename = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + kind + "-" +
            Guid.NewGuid().ToString("N") + ".zip";
        WriteArchive(id, Path.Combine(folder, filename), false);
    }

    private void WriteArchive(string id, string target, bool replace)
    {
        string source = Path.Combine(profiles, id);
        if (!HasCareer(id)) throw new InvalidOperationException("This driver has no current save to copy.");
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateDirectories(source).Any())
            throw new IOException("This driver folder has an unexpected layout.");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (string file in Directory.EnumerateFiles(source))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("The driver folder contains a file link.");
                    var entry = zip.CreateEntry(DirectoryPrefix + id + "/" + Path.GetFileName(file));
                    using (var input = File.OpenRead(file))
                    using (var output = entry.Open()) input.CopyTo(output);
                }
            }
            if (replace && File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally { File.Delete(temporary); }
    }

    private static bool Matches(string source, string archive)
    {
        try
        {
            using (var stream = File.OpenRead(archive))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            using (var sha = SHA256.Create())
            {
                string[] files = Directory.GetFiles(source);
                if (files.Length != zip.Entries.Count) return false;
                foreach (string file in files)
                {
                    string path = DirectoryPrefix + Path.GetFileName(source) + "/" + Path.GetFileName(file);
                    var entry = zip.GetEntry(path);
                    if (entry == null || entry.Length != new FileInfo(file).Length) return false;
                    using (var disk = File.OpenRead(file))
                    using (var snapshot = entry.Open())
                        if (!sha.ComputeHash(disk).SequenceEqual(sha.ComputeHash(snapshot))) return false;
                }
                return true;
            }
        }
        catch (InvalidDataException) { return false; }
    }

    internal void Restore(string archive, string selectedId)
    {
        using (Lock())
        using (var stream = File.OpenRead(archive))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            string id = null;
            long bytes = 0;
            var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(DirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Choose a career ZIP exported by this launcher.");
                string[] path = entry.FullName.Substring(DirectoryPrefix.Length).Split('/');
                if (path.Length != 2 || !IsId(path[0]) ||
                    !Regex.IsMatch(path[1], @"\A[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*\z") ||
                    Regex.IsMatch(path[1], @"\A(?:con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\.|\z)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("The ZIP contains an invalid save path.");
                if (id == null) id = path[0];
                if (!id.Equals(path[0], StringComparison.OrdinalIgnoreCase) || files.ContainsKey(path[1]))
                    throw new InvalidDataException("A career ZIP must contain one driver and no duplicate files.");
                if (entry.Length > 32 * 1024 * 1024 - bytes || files.Count >= 64)
                    throw new InvalidDataException("The ZIP is too large to be a career save.");
                bytes += entry.Length;
                files.Add(path[1], entry);
            }
            if (!files.ContainsKey(CareerFile) || files[CareerFile].Length == 0 ||
                (selectedId != null && !selectedId.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The ZIP does not contain the expected career save.");

            Directory.CreateDirectory(profiles);
            string staging = Path.Combine(Path.GetDirectoryName(profiles), ".restore-" + Guid.NewGuid().ToString("N"));
            string incoming = Path.Combine(staging, "incoming");
            string previous = Path.Combine(staging, "previous");
            string active = Path.Combine(profiles, id);
            Directory.CreateDirectory(incoming);
            try
            {
                byte[] buffer = new byte[65536];
                foreach (var file in files)
                {
                    using (var input = file.Value.Open())
                    using (var output = new FileStream(Path.Combine(incoming, file.Key), FileMode.CreateNew))
                    {
                        long remaining = file.Value.Length;
                        while (remaining > 0)
                        {
                            int count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (count == 0) throw new InvalidDataException("A file in the backup is truncated.");
                            output.Write(buffer, 0, count);
                            remaining -= count;
                        }
                        if (input.ReadByte() != -1) throw new InvalidDataException("A file in the backup has an invalid size.");
                    }
                }
                bool hadPrevious = Directory.Exists(active);
                if (hadPrevious)
                {
                    if (!HasCareer(id)) throw new IOException("That driver folder exists without a career save.");
                    CreateBackup(id, "before-restore");
                    Directory.Move(active, previous);
                }
                try { Directory.Move(incoming, active); }
                catch
                {
                    if (hadPrevious) Directory.Move(previous, active);
                    throw;
                }
                if (hadPrevious) Directory.Delete(previous, true);
            }
            finally
            {
                if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
                // If rollback itself failed, keep the previous directory for recovery.
                if (!Directory.Exists(previous)) Directory.Delete(staging);
            }
        }
    }
}
