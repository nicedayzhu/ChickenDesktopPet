using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

// This developer tool is deliberately independent of the desktop project, so
// a missing bundle can be regenerated without building or running the pet.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: AssetBundle <CS2 pak01_dir.vpk> <output directory>");
    return 2;
}
try
{
    var sourcePath = Path.GetFullPath(args[0]);
    var outputDirectory = Path.GetFullPath(args[1]);
    using var source = new Package();
    source.OptimizeEntriesForBinarySearch();
    source.Read(sourcePath);
    // Only the maintenance tool may use game search paths to resolve shared
    // dependencies from csgo/core packages or loose compiled resources.
    using var loader = new GameFileLoader(source, sourcePath);
    var entries = (source.Entries ?? throw new InvalidDataException("Empty source VPK."))
        .Values.SelectMany(group => group).ToArray();
    var roots = entries.Where(IsRoot).Select(entry => entry.GetFullPath())
        // The renderer requests these by name; they are not RERL dependencies.
        .Concat(["textures/dev/brdf_lut.vtex_c", "textures/dev/blue_noise_256.vtex_c"])
        .Order(StringComparer.Ordinal).ToArray();
    if (!roots.Contains("models/chicken/chick.vmdl_c", StringComparer.Ordinal))
        throw new InvalidDataException("The source VPK does not contain the default chick.");

    var files = new SortedDictionary<string, AssetFile>(StringComparer.Ordinal);
    var omittedReferences = new SortedSet<string>(StringComparer.Ordinal);
    var queue = new Queue<string>(roots);
    using var bundle = new Package { Version = 2 };
    while (queue.TryDequeue(out var path))
    {
        if (files.ContainsKey(path)) continue;
        using var stream = loader.GetFileStream(path)
            ?? throw new FileNotFoundException($"Missing dependency: {path}");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var data = bytes.ToArray();
        var dependencies = Array.Empty<string>();
        if (path.EndsWith("_c", StringComparison.Ordinal))
        {
            using var resource = new Resource { FileName = path };
            using var resourceStream = new MemoryStream(data, writable: false);
            resource.Read(resourceStream);
            // Valve ships references to optional GPU morph data that is absent
            // from the game packages and unused by VRF's skeletal renderer.
            var references = resource.ExternalReferences?.ResourceRefInfoList ?? [];
            foreach (var reference in references.Where(reference => reference.Name.EndsWith(".vgcxdata", StringComparison.Ordinal)))
                omittedReferences.Add(reference.Name);
            dependencies = references.Where(reference => !reference.Name.EndsWith(".vgcxdata", StringComparison.Ordinal))
                .Select(reference => CompiledPath(reference.Name))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        files.Add(path, new(path, data.Length, Hash(data), dependencies));
        bundle.AddFile(path, data);
        foreach (var dependency in dependencies) queue.Enqueue(dependency);
    }

    Directory.CreateDirectory(outputDirectory);
    var bundlePath = Path.Combine(outputDirectory, "pet_assets.vpk");
    var temporaryBundle = bundlePath + ".tmp";
    var manifestPath = Path.Combine(outputDirectory, "manifest.json");
    var temporaryManifest = manifestPath + ".tmp";
    try
    {
        using (var stream = File.Create(temporaryBundle)) bundle.Write(stream);
        // Reopen and check every byte before replacing the previous snapshot.
        using (var check = new Package())
        {
            check.Read(temporaryBundle);
            check.VerifyHashes();
            check.VerifyFileChecksums();
            foreach (var file in files.Values)
            {
                var entry = check.FindEntry(file.Path) ?? throw new InvalidDataException(file.Path);
                check.ReadEntry(entry, out var data);
                if (Hash(data) != file.Sha256) throw new InvalidDataException(file.Path);
            }
        }
        using var hashStream = File.OpenRead(temporaryBundle);
        var bundleHash = Convert.ToHexString(SHA256.HashData(hashStream)).ToLowerInvariant();
        hashStream.Close();
        var steamInfo = Path.Combine(Path.GetDirectoryName(sourcePath)!, "steam.inf");
        var sourceBuild = File.Exists(steamInfo)
            ? File.ReadAllLines(steamInfo).FirstOrDefault(line => line.StartsWith("ClientVersion=", StringComparison.Ordinal))
            : null;
        var manifest = new
        {
            schemaVersion = 1,
            bundleFile = "pet_assets.vpk",
            bundleSha256 = bundleHash,
            source = "Counter-Strike 2",
            sourceBuild,
            extractorVersion = typeof(Resource).Assembly.GetName().Version?.ToString(),
            fileCount = files.Count,
            totalBytes = files.Values.Sum(file => (long)file.Bytes),
            roots,
            omittedGpuMorphReferences = omittedReferences,
            files = files.Values,
        };
        File.WriteAllText(temporaryManifest, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }) + "\n");
        File.Move(temporaryBundle, bundlePath, overwrite: true);
        File.Move(temporaryManifest, manifestPath, overwrite: true);
        Console.WriteLine($"Bundled {files.Count} files ({new FileInfo(bundlePath).Length / 1024.0 / 1024:F2} MiB)");
        Console.WriteLine($"SHA256 {bundleHash}");
    }
    finally
    {
        File.Delete(temporaryBundle);
        File.Delete(temporaryManifest);
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static bool IsRoot(PackageEntry entry)
{
    var directory = entry.DirectoryName;
    var name = entry.FileName;
    if (directory == "models/chicken" && entry.TypeName == "vmdl_c")
        return name != "chicken_roasted" && !name.StartsWith("feather_", StringComparison.Ordinal)
            && !name.EndsWith("_animset", StringComparison.Ordinal);
    if (entry.TypeName == "vnmclip_c")
        return directory.StartsWith("animation/anims/chicken/", StringComparison.Ordinal)
            || directory == "animation/anims/egg";
    if (directory == "animation/graphs/chicken" && name == "chicken" && entry.TypeName == "vnmgraph_c") return true;
    if (directory == "panorama/scripts/popups" && name == "pet_photo_tag") return true;
    return directory == "panorama/images/icons/ui" && entry.TypeName == "vsvg_c"
        && (name.StartsWith("pet_", StringComparison.Ordinal)
            || new[] { "zoom_in", "photo", "edit_label", "expand_video", "shrink_video" }.Contains(name));
}

static string CompiledPath(string path) => path.EndsWith("_c", StringComparison.Ordinal) ? path : path + "_c";
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
internal sealed record AssetFile(string Path, int Bytes, string Sha256, string[] Dependencies);
