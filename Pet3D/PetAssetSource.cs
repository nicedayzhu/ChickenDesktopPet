using System.IO;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

namespace ChickenDesktopPet3D;

// Keep all runtime lookups inside the selected bundle. Passing no game path to
// VRF prevents gameinfo/Steam search paths from hiding missing dependencies.
internal static class PetAssetSource
{
    public const string RelativePath = "Resources/pet_assets.vpk";

    public static string ResolvePath()
    {
        var configured = Environment.GetEnvironmentVariable("CHICK_PET_VPK");
        var path = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, RelativePath)
            : configured);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "未找到小鸡资源包。请完整解压发布包，保留 EXE 旁的 Resources 文件夹。", path);
        return path;
    }

    public static GameFileLoader CreateLoader(Package package) => new(package, null);
}
