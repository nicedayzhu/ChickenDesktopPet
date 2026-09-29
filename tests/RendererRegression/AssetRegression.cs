using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ChickenDesktopPet3D;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

internal static class AssetRegression
{
    public static int Run()
    {
        var previousOverride = Environment.GetEnvironmentVariable("CHICK_PET_VPK");
        var previousGame = Environment.GetEnvironmentVariable("CHICK_CS2_VPK");
        try
        {
            Environment.SetEnvironmentVariable("CHICK_PET_VPK", null);
            // A broken legacy game path must not affect normal startup.
            Environment.SetEnvironmentVariable("CHICK_CS2_VPK", "Z:/no-cs2/pak01_dir.vpk");
            var path = PetAssetSource.ResolvePath();
            if (path != Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PetAssetSource.RelativePath)))
                throw new InvalidDataException("Default bundle must be relative to the EXE.");
            Environment.SetEnvironmentVariable("CHICK_PET_VPK", path);
            if (PetAssetSource.ResolvePath() != path) throw new InvalidDataException("Bundle override failed.");
            Environment.SetEnvironmentVariable("CHICK_PET_VPK", path + ".missing");
            try
            {
                PetAssetSource.ResolvePath();
                throw new InvalidDataException("Missing override silently fell back.");
            }
            catch (FileNotFoundException) { }
            Environment.SetEnvironmentVariable("CHICK_PET_VPK", null);

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "manifest.json")));
            var manifest = document.RootElement;
            using (var stream = File.OpenRead(path))
                if (Hash(SHA256.HashData(stream)) != manifest.GetProperty("bundleSha256").GetString())
                    throw new InvalidDataException("Bundle SHA256 mismatch.");
            using var package = new Package(); package.Read(path);
            package.VerifyHashes();
            package.VerifyFileChecksums();
            using var loader = PetAssetSource.CreateLoader(package);
            if (loader.GameName is not null) throw new InvalidDataException("Loader discovered a local game.");
            var files = manifest.GetProperty("files").EnumerateArray().ToDictionary(file => file.GetProperty("path").GetString()!);
            if (package.Entries!.Values.Sum(entries => entries.Count) != files.Count ||
                manifest.GetProperty("fileCount").GetInt32() != files.Count)
                throw new InvalidDataException("Manifest file count mismatch.");
            var omitted = manifest.GetProperty("omittedGpuMorphReferences").EnumerateArray()
                .Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
            foreach (var (name, file) in files)
            {
                var entry = package.FindEntry(name) ?? throw new InvalidDataException($"Missing {name}");
                package.ReadEntry(entry, out var data);
                if (data.Length != file.GetProperty("bytes").GetInt32() ||
                    Hash(SHA256.HashData(data)) != file.GetProperty("sha256").GetString())
                    throw new InvalidDataException($"Changed {name}");
                var declared = file.GetProperty("dependencies").EnumerateArray().Select(value => value.GetString()!).ToHashSet();
                if (declared.Any(dependency => !files.ContainsKey(dependency)))
                    throw new InvalidDataException($"Incomplete dependencies for {name}");
                using var resource = loader.LoadFile(name) ?? throw new InvalidDataException($"Unreadable {name}");
                foreach (var reference in resource.ExternalReferences?.ResourceRefInfoList ?? [])
                {
                    if (omitted.Contains(reference.Name) && reference.Name.EndsWith(".vgcxdata", StringComparison.Ordinal)) continue;
                    var dependency = reference.Name.EndsWith("_c", StringComparison.Ordinal) ? reference.Name : reference.Name + "_c";
                    if (!declared.Contains(dependency)) throw new InvalidDataException($"Undeclared dependency: {dependency}");
                }
            }
            var ui = PetUiResources.Load(package, loader);
            var catalog = new PetCatalog(package, loader, ui);
            if (catalog.Find("chick") is null || catalog.Appearances.Count < 6 ||
                ui.Activities.Count != 7 || ui.GraphClips.Count < 7 || ui.Icons.Count < 18)
                throw new InvalidDataException("Pet catalog or official UI data is incomplete.");
            foreach (var clips in ui.GraphClips.Values)
                foreach (var clip in clips)
                    if (package.FindEntry(clip + "_c") is null) throw new InvalidDataException($"Missing action {clip}");
            Console.WriteLine($"PASS standalone bundle: {files.Count} files, {catalog.Appearances.Count} appearances, {ui.Icons.Count} icons; hashes and dependency closure valid; no local game search paths");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("CHICK_PET_VPK", previousOverride);
            Environment.SetEnvironmentVariable("CHICK_CS2_VPK", previousGame);
        }
    }

    private static string Hash(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
}
