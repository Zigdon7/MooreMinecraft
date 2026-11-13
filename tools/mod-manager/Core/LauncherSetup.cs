using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Saturn.Manager;

/// <summary>Official Minecraft Launcher integration. Never runs downloaded installers or touches accounts.</summary>
public sealed class LauncherSetup(HttpClient client)
{
    public const string ProfileKey = "saturn-higdon-server", ProfileName = "Higdon Server";
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
    public static string DefaultGame => GameFolder(false);
    public static string GameFolder(bool staging) => Path.Combine(DefaultRoot, staging ? "higdon-server-staging" : "higdon-server");
    public static string Name(bool staging) => staging ? "Higdon Server (Staging)" : ProfileName;
    static string Key(bool staging) => staging ? ProfileKey + "-staging" : ProfileKey;
    static string VersionId(PackManifest p) => $"higdon-fabric-loader-{p.FabricLoader}-{p.Minecraft}";
    static void Validate(PackManifest p)
    {
        p.Validate();
        foreach (string s in new[] { p.Minecraft, p.FabricLoader })
            if (!Regex.IsMatch(s, @"\A[0-9][A-Za-z0-9.+_-]{0,63}\z")) throw new InvalidDataException("Invalid Minecraft or Fabric version");
    }
    static IEnumerable<string> ProfileFiles(string root)
    {
        var files = new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" }.Select(n => Path.Combine(root, n)).Where(File.Exists).ToArray();
        return files.Length == 0 ? [Path.Combine(root, "launcher_profiles.json")] : files;
    }
    static void NoLinks(string path)
    {
        for (var p = new DirectoryInfo(Path.GetFullPath(path)); p is not null; p = p.Parent) Rules.NoLink(p.FullName);
    }
    public static bool Ready(string root, string game, PackManifest pack, bool staging = false)
    {
        try
        {
            Validate(pack);
            string id = VersionId(pack);
            if (!File.Exists(Path.Combine(root, "versions", id, id + ".json"))) return false;
            return ProfileFiles(root).All(f => File.Exists(f) && JsonNode.Parse(File.ReadAllText(f))?["profiles"]?[Key(staging)] is JsonObject p
                && p["name"]?.GetValue<string>() == Name(staging) && p["lastVersionId"]?.GetValue<string>() == id
                && string.Equals(p["gameDir"]?.GetValue<string>(), Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }
    public async Task<JsonObject> Fetch(PackManifest pack, CancellationToken token)
    {
        Validate(pack);
        var uri = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(pack.Minecraft)}/{Uri.EscapeDataString(pack.FabricLoader)}/profile/json";
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[8192]; int n;
        while ((n = await stream.ReadAsync(buffer, token)) > 0) { if (memory.Length + n > 1048576) throw new InvalidDataException("Fabric profile too large"); memory.Write(buffer, 0, n); }
        var json = JsonNode.Parse(memory.ToArray()) as JsonObject ?? throw new InvalidDataException("Invalid Fabric profile");
        if (json["id"]?.GetValue<string>() != $"fabric-loader-{pack.FabricLoader}-{pack.Minecraft}" || json["inheritsFrom"]?.GetValue<string>() != pack.Minecraft
            || json["mainClass"]?.GetValue<string>() != "net.fabricmc.loader.impl.launch.knot.KnotClient" || json["libraries"] is not JsonArray { Count: > 0 })
            throw new InvalidDataException("Fabric returned a profile for another version");
        // This is the official Fabric metadata; the official launcher fetches the game, runtime and libraries on Play.
        json["id"] = VersionId(pack);
        return json;
    }
    public static void EnsureClosed()
    {
        foreach (var process in System.Diagnostics.Process.GetProcesses())
            using (process)
                if (new[] { "MinecraftLauncher", "Minecraft", "java", "javaw" }.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                    throw new IOException("Close Minecraft and the Minecraft Launcher, then click Install again.");
    }
    public static void Apply(string root, string game, PackManifest pack, JsonObject fabric, bool staging = false)
    {
        Validate(pack); root = Path.GetFullPath(root); game = Path.GetFullPath(game);
        NoLinks(root); NoLinks(game);
        string id = VersionId(pack);
        if (fabric["id"]?.GetValue<string>() != id || fabric["inheritsFrom"]?.GetValue<string>() != pack.Minecraft) throw new InvalidDataException("Wrong prepared Fabric profile");
        Directory.CreateDirectory(root);
        // Cooperative setup lock + exclusive launcher file handles; stale content is never merged from a prior read.
        string lockFile = Path.Combine(root, ".higdon-setup.lock"); Rules.NoLink(lockFile);
        using var setupLock = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var opened = new List<(string Path, FileStream File, byte[] Before, JsonObject Json)>();
        string versionPath = Path.Combine(root, "versions", id, id + ".json");
        NoLinks(Path.GetDirectoryName(versionPath)!); Rules.NoLink(versionPath);
        try
        {
            foreach (string path in ProfileFiles(root))
            {
                Rules.NoLink(path);
                var f = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    if (f.Length > 4 * 1048576) throw new InvalidDataException("Launcher profile file too large");
                    using var data = new MemoryStream(); f.CopyTo(data); byte[] bytes = data.ToArray();
                    var json = bytes.Length == 0 ? new JsonObject() : JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Invalid launcher profiles");
                    if (json["profiles"] is null) json["profiles"] = new JsonObject();
                    if (json["profiles"] is not JsonObject profiles) throw new InvalidDataException("Invalid launcher profile list");
                    if (profiles[Key(staging)] is not null && profiles[Key(staging)] is not JsonObject) throw new InvalidDataException("Profile key is not an object");
                    var p = profiles[Key(staging)] as JsonObject ?? new JsonObject();
                    if (profiles[Key(staging)] is null) profiles[Key(staging)] = p;
                    p["name"] = Name(staging); p["type"] = "custom"; p["lastVersionId"] = id; p["gameDir"] = game; p["icon"] = "Grass";
                    opened.Add((path, f, bytes, json));
                }
                catch { f.Dispose(); throw; }
            }
            Directory.CreateDirectory(game); Directory.CreateDirectory(Path.GetDirectoryName(versionPath)!);
            Rules.NoLink(versionPath + ".new");
            Rules.Write(versionPath, fabric);
            foreach (var entry in opened)
            {
                if (entry.Before.Length > 0)
                {
                    string backup = entry.Path + ".higdon-backup-" + Guid.NewGuid().ToString("N");
                    File.WriteAllBytes(backup, entry.Before);
                }
                byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry.Json, Rules.Json);
                entry.File.Position = 0; entry.File.Write(bytes); entry.File.SetLength(bytes.Length); entry.File.Flush(true);
            }
        }
        catch
        {
            foreach (var entry in opened) { entry.File.Position = 0; entry.File.Write(entry.Before); entry.File.SetLength(entry.Before.Length); entry.File.Flush(true); }
            throw;
        }
        finally { foreach (var entry in opened) entry.File.Dispose(); }
    }
}
