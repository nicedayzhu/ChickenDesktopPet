using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ChickenDesktopPet3D;

internal sealed record PetActivity(string Id, string Label, string Icon, string Activity, int? Variation);

// Read data on the render thread, then hand immutable SVG bytes to WPF. No game
// JavaScript is executed and no extracted Valve resources enter the release.
internal sealed class PetUiResources
{
    public static readonly PetActivity[] DefaultActivities =
    [
        new("sit", "坐下", "pet_activity_sit", "trick", 2),
        new("panic", "惊慌", "pet_activity_panic", "panic", null),
        new("wag", "摇尾", "pet_activity_wag", "trick", 9),
        new("moonwalk", "跳舞", "pet_activity_dance", "trick", 5),
        new("jump", "跳跃", "pet_activity_jump", "trick", 11),
        new("kick", "踢腿", "pet_activity_kick", "trick", 12),
        new("fly", "飞翔", "pet_activity_fly", "trick", 1),
    ];

    public IReadOnlyList<PetActivity> Activities { get; private set; } = DefaultActivities;
    public Dictionary<string, byte[]> Icons { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string[]> GraphClips { get; } = new(StringComparer.Ordinal);

    public static PetUiResources Load(Package package, GameFileLoader loader)
    {
        var result = new PetUiResources();
        try
        {
            var script = package.Entries?.Values.SelectMany(entries => entries).FirstOrDefault(entry =>
                entry.DirectoryName == "panorama/scripts/popups" && entry.FileName == "pet_photo_tag");
            if (script is not null)
            {
                var extension = script.TypeName.EndsWith("_c", StringComparison.Ordinal) ? script.TypeName[..^2] : script.TypeName;
                using var resource = loader.LoadFileCompiled($"{script.DirectoryName}/{script.FileName}.{extension}");
                if (resource?.DataBlock is Panorama data)
                    result.ReadActivities(Encoding.UTF8.GetString(data.Data));
            }
        }
        catch (Exception ex) { ErrorLog.Trace($"official pet UI data fallback: {ex.Message}"); }
        try
        {
            using var graph = loader.LoadFileCompiled("animation/graphs/chicken/chicken.vnmgraph");
            if (graph?.DataBlock is BinaryKV3 kv) result.ReadGraph(kv);
        }
        catch (Exception ex) { ErrorLog.Trace($"official pet graph data fallback: {ex.Message}"); }

        var icons = result.Activities.Select(activity => activity.Icon).Concat(
            ["pet_feed", "pet_activity_sleep", "pet_activity_trick", "zoom_in", "photo", "edit_label",
             "pet_book", "expand_video", "shrink_video", "pet_chick", "pet_egg"]);
        foreach (var name in icons.Distinct(StringComparer.Ordinal))
        {
            try
            {
                using var resource = loader.LoadFileCompiled($"panorama/images/icons/ui/{name}.vsvg");
                if (resource?.DataBlock is Panorama svg) result.Icons[name] = svg.Data.ToArray();
            }
            catch (Exception ex) { ErrorLog.Trace($"icon fallback {name}: {ex.Message}"); }
        }
        return result;
    }

    private void ReadActivities(string source)
    {
        var block = Regex.Match(source, @"PetPhotoTag\.ACTIVITIES\s*=\s*\[(.*?)\];", RegexOptions.Singleline);
        if (!block.Success) return;
        var rows = Regex.Matches(block.Groups[1].Value, @"\{\s*id\s*:\s*\d+\s*,(.*?)(?=\{\s*id\s*:|$)", RegexOptions.Singleline);
        var parsed = new List<PetActivity>();
        foreach (Match row in rows)
        {
            string Value(string key) => Regex.Match(row.Groups[1].Value,
                key + "\\s*:\\s*['\"]([^'\"]+)['\"]").Groups[1].Value;
            var name = Value("name");
            var activity = Value("activity");
            var icon = Value("icon");
            if (!Regex.IsMatch(name, @"^[a-z][a-z0-9_]*$") || string.IsNullOrEmpty(activity) ||
                !Regex.IsMatch(icon, @"^[a-z][a-z0-9_]*$")) continue;
            var variation = Regex.Match(row.Groups[1].Value, @"variation\s*:\s*(\d+)");
            var label = DefaultActivities.FirstOrDefault(item => item.Id == name)?.Label ?? name;
            parsed.Add(new(name, label, icon, activity, variation.Success ? int.Parse(variation.Groups[1].Value) : null));
        }
        if (parsed.Count > 0) Activities = parsed.DistinctBy(item => item.Id).ToArray();
    }

    private void ReadGraph(BinaryKV3 graph)
    {
        var data = graph.Data.Root;
        var nodes = data.GetArray("m_nodes") ?? [];
        var paths = data.GetArray<string>("m_nodePaths") ?? [];
        var resources = data.GetArray<string>("m_resources") ?? [];
        var nodeMap = nodes.ToDictionary(node => node.GetInt32Property("m_nNodeIdx"));
        var variationNode = Array.IndexOf(paths, "action_variation");
        string[] Resolve(long[] options) => options.Select(option =>
        {
            if (!nodeMap.TryGetValue((int)option, out var clip)) return null;
            var slot = clip.GetInt32Property("m_nDataSlotIdx", -1);
            return slot >= 0 && slot < resources.Length ? resources[slot] : null;
        }).Where(path => path is not null).Cast<string>().ToArray();

        var selectors = nodes.Where(node => node.GetStringProperty("_class").Contains("ParameterizedClipSelector", StringComparison.Ordinal))
            .Select(node => (Node: node, Index: node.GetInt32Property("m_nNodeIdx")))
            .Where(item => item.Index >= 0 && item.Index < paths.Length).ToArray();
        var tricks = selectors.FirstOrDefault(item => paths[item.Index].Contains("/all tricks/", StringComparison.Ordinal) &&
            item.Node.GetInt32Property("m_parameterNodeIdx", -1) == variationNode);
        if (tricks.Node is not null)
        {
            var options = tricks.Node.GetIntegerArray("m_optionNodeIndices") ?? [];
            foreach (var activity in Activities.Where(activity => activity.Activity == "trick" && activity.Variation is not null))
            {
                var index = activity.Variation!.Value;
                if (index >= 0 && index < options.Length)
                {
                    var clips = Resolve([options[index]]);
                    if (clips.Length > 0) GraphClips[activity.Id] = clips;
                }
            }
        }
        var panic = selectors.Where(item => paths[item.Index].Contains("/Panic/", StringComparison.Ordinal))
            .SelectMany(item => Resolve(item.Node.GetIntegerArray("m_optionNodeIndices") ?? []))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (panic.Length > 0) GraphClips["panic"] = panic;
        ErrorLog.Trace($"official activities: {Activities.Count}, graph mappings: {GraphClips.Count}");
    }
}
