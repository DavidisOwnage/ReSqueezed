using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal static class DiscValidator
{
    internal const string SupportedDisc = "Juiced retail (USA / Europe, English/French/German/Spanish/Italian)";
    internal const string XbeMd5 = "3F4399A5947E98B9681DDF4F08241A56";
    internal const string XbeSha256 = "90912DE89F5A105D15568D38A0B5901962EC4DF9D41D99B6551A99FE947462A2";
    private const int XbeLength = 3518464;
    private static readonly string[] RequiredFiles = { "ui.dat", "scripts.dat", "textures.dat", "mesh.dat" };
    private static readonly string[] RequiredDirectories = { "audio", "cars", "movies", "tracks" };

    internal static void ValidateFolder(string folder)
    {
        string path = Path.Combine(folder, "default.xbe");
        if (!File.Exists(path))
            throw new InvalidOperationException("Select the extracted Xbox disc folder containing default.xbe.");
        using (var stream = File.OpenRead(path))
            ValidateExecutable(stream, stream.Length);
        foreach (string name in RequiredFiles)
            if (!File.Exists(Path.Combine(folder, name)) || new FileInfo(Path.Combine(folder, name)).Length == 0)
                throw new InvalidOperationException("The disc extraction is missing or has an empty " + name + ".");
        foreach (string name in RequiredDirectories)
            if (!Directory.Exists(Path.Combine(folder, name)))
                throw new InvalidOperationException("The disc extraction is missing the " + name + " folder.");
    }

    internal static void ValidateIso(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
        {
            long partition = -1;
            foreach (long candidate in new long[] { 0, 0xFD90, 0x30600, 0xFD90000, 0x18300000 })
            {
                if (candidate + 0x10800 > stream.Length) continue;
                stream.Position = candidate + 0x10000;
                if (Encoding.ASCII.GetString(reader.ReadBytes(20)) == "MICROSOFT*XBOX*MEDIA")
                {
                    partition = candidate;
                    break;
                }
            }
            if (partition < 0)
                throw new InvalidOperationException("This file is not a supported Xbox ISO. Choose a Juiced retail XISO, full disc ISO, or extracted folder.");
            uint rootSector = reader.ReadUInt32();
            uint rootSize = reader.ReadUInt32();
            long directoryOffset = partition + (long)rootSector * 2048;
            if (rootSize < 14 || rootSize > 16 * 1024 * 1024 || directoryOffset > stream.Length - rootSize)
                throw new InvalidOperationException("The Xbox ISO has an invalid or truncated root directory.");
            stream.Position = directoryOffset;
            byte[] data = reader.ReadBytes((int)rootSize);
            var entries = new Dictionary<string, IsoEntry>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<int>();
            var visited = new HashSet<int>();
            pending.Push(0);
            while (pending.Count > 0)
            {
                int offset = pending.Pop();
                if (!visited.Add(offset) || offset < 0 || offset > data.Length - 14)
                    throw new InvalidOperationException("The Xbox ISO has a damaged directory tree.");
                ushort left = BitConverter.ToUInt16(data, offset);
                ushort right = BitConverter.ToUInt16(data, offset + 2);
                int nameLength = data[offset + 13];
                if (nameLength == 0 || offset + 14 + nameLength > data.Length || left == 65535 || right == 65535)
                    throw new InvalidOperationException("The Xbox ISO has an invalid directory entry.");
                string name = Encoding.ASCII.GetString(data, offset + 14, nameLength);
                var entry = new IsoEntry {
                    Position = partition + (long)BitConverter.ToUInt32(data, offset + 4) * 2048,
                    Length = BitConverter.ToUInt32(data, offset + 8),
                    Directory = (data[offset + 12] & 0x10) != 0
                };
                if (entry.Position < partition || entry.Position > stream.Length - entry.Length || entries.ContainsKey(name))
                    throw new InvalidOperationException("The Xbox ISO has a truncated or duplicate file entry.");
                entries.Add(name, entry);
                if (left != 0) pending.Push(left * 4);
                if (right != 0) pending.Push(right * 4);
            }
            IsoEntry executable;
            if (!entries.TryGetValue("default.xbe", out executable) || executable.Directory)
                throw new InvalidOperationException("The selected Xbox ISO does not contain Juiced's default.xbe.");
            stream.Position = executable.Position;
            ValidateExecutable(stream, executable.Length);
            foreach (string name in RequiredFiles)
            {
                IsoEntry entry;
                if (!entries.TryGetValue(name, out entry) || entry.Directory || entry.Length == 0)
                    throw new InvalidOperationException("The Xbox ISO is missing " + name + ".");
            }
            foreach (string name in RequiredDirectories)
            {
                IsoEntry entry;
                if (!entries.TryGetValue(name, out entry) || !entry.Directory)
                    throw new InvalidOperationException("The Xbox ISO is missing the " + name + " folder.");
            }
        }
    }

    private static void ValidateExecutable(Stream stream, long length)
    {
        if (length != XbeLength)
            throw new InvalidOperationException("Unsupported Xbox executable. Select " + SupportedDisc + ".");
        byte[] data = new byte[XbeLength];
        int read = 0;
        while (read < data.Length)
        {
            int count = stream.Read(data, read, data.Length - read);
            if (count == 0) throw new InvalidOperationException("The selected disc is truncated.");
            read += count;
        }
        string md5, sha256;
        using (var hash = MD5.Create()) md5 = Hex(hash.ComputeHash(data));
        using (var hash = SHA256.Create()) sha256 = Hex(hash.ComputeHash(data));
        if (md5 != XbeMd5 || sha256 != XbeSha256)
            throw new InvalidOperationException("Unsupported or modified game executable. Select " + SupportedDisc +
                ". Detected default.xbe MD5 " + md5 + ".");
    }

    private static string Hex(byte[] hash) { return BitConverter.ToString(hash).Replace("-", ""); }

    private sealed class IsoEntry
    {
        internal long Position;
        internal long Length;
        internal bool Directory;
    }
}
