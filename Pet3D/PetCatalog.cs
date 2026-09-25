using System.IO;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ChickenDesktopPet3D;

internal enum PetKind { Chicken, Egg, Static }

internal sealed record PetAppearance(
    string Id,
    string ModelId,
    string ModelPath,
    string GroupLabel,
    string Label,
    string? Skin,
    PetKind Kind);

// Read the installed game's VPK on every launch. New official models, skins and
// clips can appear without shipping extracted game resources or a new catalog.
internal sealed class PetCatalog
{
    public const string DefaultAppearanceId = "chick";

    private readonly (string Directory, string Name, string Path)[] clips;

    public IReadOnlyList<PetAppearance> Appearances { get; }

    public PetCatalog(Package package, GameFileLoader loader)
    {
        var appearances = new List<PetAppearance>();
        if (package.Entries is { } entries && entries.TryGetValue("vmdl_c", out var models))
        {
            foreach (var entry in models.Where(entry => entry.DirectoryName == "models/chicken")
                .OrderBy(entry => SortOrder(entry.FileName)).ThenBy(entry => entry.FileName, StringComparer.Ordinal))
            {
                var id = entry.FileName;
                if (id == "chicken_roasted" || id.StartsWith("feather_", StringComparison.Ordinal) ||
                    id.EndsWith("_animset", StringComparison.Ordinal))
                    continue;

                var modelPath = $"{entry.DirectoryName}/{id}.vmdl";
                try
                {
                    using var resource = loader.LoadFileCompiled(modelPath);
                    if (resource?.DataBlock is not Model model ||
                        (!model.GetEmbeddedMeshesAndLoD().Any() && !model.GetReferenceMeshNamesAndLoD().Any()))
                        continue;

                    var group = DisplayName(id);
                    var kind = id == "chicknegg" || id.StartsWith("egg_", StringComparison.Ordinal)
                        ? id == "chicknegg" ? PetKind.Egg : PetKind.Static
                        : model.Skeleton.Bones.Length <= 1 ? PetKind.Static : PetKind.Chicken;
                    var skins = model.GetMaterialGroups().Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
                    if (skins.Length == 0) skins = ["default"];
                    foreach (var skin in skins)
                    {
                        var isDefault = skin == "default";
                        appearances.Add(new PetAppearance(
                            isDefault ? id : $"{id}:{skin}", id, modelPath, group,
                            isDefault ? "默认" : SkinName(id, skin), isDefault ? null : skin, kind));
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.Write(new InvalidDataException($"跳过无法读取的 CS2 模型 {modelPath}", ex));
                }
            }
        }

        Appearances = appearances;
        clips = package.Entries is { } clipIndex && clipIndex.TryGetValue("vnmclip_c", out var clipEntries)
            ? clipEntries.Where(entry => entry.DirectoryName.StartsWith("animation/anims/chicken/", StringComparison.Ordinal)
                    || entry.DirectoryName == "animation/anims/egg")
                .Select(entry => (entry.DirectoryName, entry.FileName, $"{entry.DirectoryName}/{entry.FileName}.vnmclip"))
                .ToArray()
            : [];
    }

    public PetAppearance? Find(string? id) => Appearances.FirstOrDefault(item => item.Id == id);

    public IEnumerable<string> ClipCandidates(PetAppearance appearance, string action)
    {
        if (appearance.Kind == PetKind.Static) yield break;

        var isEgg = appearance.Kind == PetKind.Egg;
        var folder = isEgg ? "animation/anims/egg" : action == "feed"
            ? "animation/anims/chicken/ui" : "animation/anims/chicken/world";
        var (preferred, prefix) = isEgg ? EggRule(action) : ChickenRule(action, appearance.ModelId == "chick");
        if (prefix is null) yield break;

        var matching = clips.Where(clip => clip.Directory == folder).ToArray();
        foreach (var name in preferred)
        {
            foreach (var clip in matching.Where(clip => clip.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                yield return clip.Path;
        }
        foreach (var clip in matching.Where(clip => clip.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !preferred.Contains(clip.Name, StringComparer.OrdinalIgnoreCase)
            && !clip.Name.Contains("water", StringComparison.OrdinalIgnoreCase)
            && !clip.Name.Contains("non_additive", StringComparison.OrdinalIgnoreCase)
            && !clip.Name.Contains("sad", StringComparison.OrdinalIgnoreCase)))
            yield return clip.Path;
    }

    private static (string[] Preferred, string? Prefix) ChickenRule(string action, bool baby) => action switch
    {
        "idle" => (["chick_idle01", "chick_idle03"], "chick_idle"),
        "idle2" => (["chick_idle02", "chick_idle04"], "chick_idle"),
        "squat" => (["chick_squat_loop04", "chick_squat_loop01"], "chick_squat_loop"),
        "walk" => (["chick_walk", "chick_run"], "chick_walk"),
        "react" => (["chick_react01", "chick_react03"], "chick_react"),
        "react2" => (["chick_react02", "chick_react04"], "chick_react"),
        "trick" => (["chick_trick01", "chick_trick02"], "chick_trick"),
        "trick2" => (["chick_trick03", "chick_trick04"], "chick_trick"),
        "feed" => (baby ? ["chickbaby_feed02", "chick_feed01"] : ["chick_feed01", "chickbaby_feed02"],
            baby ? "chickbaby_feed" : "chick_feed"),
        "sleep" => (["chick_sleep_loop01"], "chick_sleep_loop"),
        _ => ([], null),
    };

    private static (string[] Preferred, string? Prefix) EggRule(string action) => action switch
    {
        "idle" => (["chick_egg_idle_phase02", "chick_egg_idle_phase01_still"], "chick_egg_idle"),
        "idle2" => (["chick_egg_idle_phase03", "chick_egg_idle_phase04"], "chick_egg_idle"),
        "react" => (["chick_egg_reaction01"], "chick_egg_reaction"),
        "react2" => (["chick_egg_reaction02"], "chick_egg_reaction"),
        "trick" => (["chick_egg_reaction03", "chick_egg_reaction04"], "chick_egg_reaction"),
        "trick2" => (["chick_egg_reaction05", "chick_egg_reaction06"], "chick_egg_reaction"),
        "sleep" => (["chick_egg_idle_phase01_still"], "chick_egg_idle"),
        _ => ([], null),
    };

    private static int SortOrder(string id) => id switch
    {
        "chick" => 0,
        "chicken" => 1,
        "chicken_polish" => 2,
        "chicken_silkie" => 3,
        "chicknegg" => 4,
        "egg_pristine" => 5,
        _ => 100,
    };

    private static string DisplayName(string id) => id switch
    {
        "chick" => "小鸡",
        "chicken" => "普通鸡",
        "chicken_polish" => "波兰鸡",
        "chicken_silkie" => "丝羽鸡",
        "chicknegg" => "破壳蛋",
        "egg_pristine" => "完整鸡蛋（静态）",
        _ => id.Replace('_', ' '),
    };

    private static string SkinName(string id, string skin) => $"羽色 {skin}";
}
