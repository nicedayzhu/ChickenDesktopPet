using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace ChickenDesktopPet3D;

internal sealed record PetPhoto(string Path, string Name, string PetId, string Background, DateTime Taken);

internal sealed class PhotoLibrary
{
    public string DirectoryPath { get; } = Environment.GetEnvironmentVariable("CHICK_PHOTO_PATH") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CS2 鸡桌宠");

    public Task<string> SaveAsync(BitmapSource snapshot, string petId, string name, string background)
    {
        if (!snapshot.IsFrozen) snapshot.Freeze();
        return Task.Run(() =>
        {
            Directory.CreateDirectory(DirectoryPath);
            var taken = DateTime.Now;
            var file = Path.Combine(DirectoryPath, $"chicken-{taken:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..4]}.png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(snapshot));
            using (var stream = File.Create(file)) encoder.Save(stream);
            File.WriteAllText(Path.ChangeExtension(file, ".json"), JsonSerializer.Serialize(new PetPhoto(file, name, petId, background, taken)));
            return file;
        });
    }

    public IReadOnlyList<PetPhoto> ReadRecent()
    {
        if (!Directory.Exists(DirectoryPath)) return [];
        var results = new List<PetPhoto>();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "chicken-*.png").OrderByDescending(File.GetLastWriteTimeUtc).Take(60))
        {
            try
            {
                var sidecar = Path.ChangeExtension(file, ".json");
                var photo = File.Exists(sidecar) ? JsonSerializer.Deserialize<PetPhoto>(File.ReadAllText(sidecar)) : null;
                results.Add(photo is null ? new(file, "鸡宠物", "", "", File.GetLastWriteTime(file)) : photo with { Path = file });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { ErrorLog.Trace(ex.Message); }
        }
        return results;
    }

    public void OpenFolder()
    {
        Directory.CreateDirectory(DirectoryPath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DirectoryPath) { UseShellExecute = true });
    }
}
