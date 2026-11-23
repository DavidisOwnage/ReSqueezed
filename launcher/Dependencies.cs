using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

internal static class Dependencies
{
    [DataContract]
    internal sealed class Dependency
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Url { get; set; }
        [DataMember] public string Sha256 { get; set; }
        [DataMember] public string Prefix { get; set; }
        [DataMember] public string Destination { get; set; }
        [DataMember] public string Probe { get; set; }
        [DataMember] public long Size { get; set; }
    }

    internal static Dependency[] Catalog()
    {
        using (var stream = typeof(Dependencies).Assembly.GetManifestResourceStream("Dependencies"))
            return (Dependency[])new DataContractJsonSerializer(typeof(Dependency[])).ReadObject(stream);
    }

    internal static void Ensure(Action<string> status)
    {
        string tools = Path.Combine(Builder.Package, "tools");
        Directory.CreateDirectory(tools);
        try
        {
            using (var setupLock = new FileStream(Path.Combine(tools, ".setup.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                foreach (Dependency item in Catalog()) Install(item, status);
        }
        catch (IOException ex)
        {
            throw new IOException("Dependency setup could not finish. Check free disk space and close any other launcher building here. " + ex.Message, ex);
        }
    }

    private static void Install(Dependency item, Action<string> status)
    {
        string destination = Inside(Builder.Package, item.Destination);
        string marker = Path.Combine(destination, ".juiced-dependency");
        string probe = Inside(destination, item.Probe);
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == item.Sha256 && File.Exists(probe))
            return;
        // A sibling staging directory keeps failed downloads and partial installs
        // from becoming usable tools. Published dependencies remain reusable.
        string stage = destination + ".setup-" + Guid.NewGuid().ToString("N");
        string archive = stage + ".zip";
        string backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        try
        {
            status("Downloading " + item.Name + "…");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(item.Url);
            request.UserAgent = "ReSqueezed-Launcher/0.1a";
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var source = response.GetResponseStream())
            using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write))
            {
                if (response.ResponseUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException("The dependency download redirected away from HTTPS.");
                byte[] buffer = new byte[1024 * 1024];
                long received = 0;
                int lastPercent = -1;
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                {
                    received += count;
                    if (received > item.Size)
                        throw new InvalidDataException(item.Name + " exceeded its expected download size.");
                    output.Write(buffer, 0, count);
                    int percent = (int)(received * 100 / item.Size);
                    if (percent != lastPercent)
                    {
                        status("Downloading " + item.Name + " · " + percent + "%");
                        lastPercent = percent;
                    }
                }
                if (received != item.Size)
                    throw new InvalidDataException(item.Name + " download was incomplete. Build again to retry.");
            }
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(archive))
                if (!String.Equals(BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""),
                    item.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(item.Name + " failed its SHA-256 check. Build again to retry.");
            status("Installing " + item.Name + "…");
            Directory.CreateDirectory(stage);
            using (var zip = ZipFile.OpenRead(archive))
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string name = entry.FullName.Replace((char)92, (char)47);
                    if (!name.StartsWith(item.Prefix, StringComparison.Ordinal)) continue;
                    name = name.Substring(item.Prefix.Length);
                    if (name.Length == 0 || name.EndsWith("/", StringComparison.Ordinal)) continue;
                    string path = Inside(stage, name);
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new InvalidDataException("The dependency archive contains a symbolic link.");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path, false);
                }
            if (!File.Exists(Inside(stage, item.Probe)))
                throw new InvalidDataException(item.Name + " is missing its expected program.");
            if (item.Id == "python")
            {
                string config = Path.Combine(stage, "python312._pth");
                File.AppendAllText(config, Environment.NewLine + "Lib/site-packages" + Environment.NewLine);
            }
            File.WriteAllText(Path.Combine(stage, ".juiced-dependency"), item.Sha256 + Environment.NewLine);
            bool replaced = Directory.Exists(destination);
            if (replaced) Directory.Move(destination, backup);
            try { Directory.Move(stage, destination); }
            catch
            {
                if (replaced) Directory.Move(backup, destination);
                throw;
            }
            if (replaced) Directory.Delete(backup, true);
        }
        catch (WebException ex)
        {
            throw new InvalidOperationException("Could not download " + item.Name +
                " from its official source. Check the internet connection and choose Build to retry. " + ex.Message, ex);
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }

    private static string Inside(string root, string relative)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A dependency path escapes its installation folder.");
        return path;
    }
}
