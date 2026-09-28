using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChickenDesktopPet3D;

internal static class FramingRegression
{
    public static int Run(string? output)
    {
        var directory = Path.GetFullPath(output ?? "research/framing-regression");
        Directory.CreateDirectory(directory);
        string[] models = ["chick", "chicken", "chicken_polish", "chicken_silkie", "chicknegg", "egg_pristine"];
        using var renderer = new ChickRenderer();
        var clock = Stopwatch.StartNew();
        var model = 0;
        var action = 0;
        var actions = new List<string>();
        var started = 0.0;
        var frames = 0;
        var clipped = 0;
        var failed = false;
        var total = 0;
        (System.Numerics.Vector3 Center, float Distance) lockedFraming = default;
        using var timeout = new System.Threading.Timer(_ =>
        {
            failed = true; Console.Error.WriteLine("FAIL framing timeout"); renderer.Dispose();
        }, null, TimeSpan.FromSeconds(450), System.Threading.Timeout.InfiniteTimeSpan);
        void BeginAction()
        {
            frames = clipped = 0;
            started = clock.Elapsed.TotalSeconds;
            renderer.Play(actions[action], true);
        }
        renderer.AppearancesReady += all =>
        {
            if (all.Any(a => a.ModelId == "chicken_roasted")) { failed = true; renderer.Dispose(); }
        };
        renderer.AppearanceChanged += (_, available) =>
        {
            actions = new[] { "idle", "trick", "feed", "sleep", "sit", "panic", "wag", "moonwalk", "jump", "kick", "fly" }.Where(available.Contains).ToList();
            if (actions.Count == 0) actions.Add("idle");
            action = 0;
            BeginAction();
        };
        renderer.Failed += ex => { failed = true; Console.Error.WriteLine(ex); renderer.Dispose(); };
        renderer.AppearanceFailed += (_, ex) => { failed = true; Console.Error.WriteLine(ex); renderer.Dispose(); };
        renderer.FrameReady += pixels =>
        {
            try
            {
                frames++; total++;
                if (frames == 3) lockedFraming = renderer.CameraFraming;
                if (frames > 3 && (System.Numerics.Vector3.Distance(lockedFraming.Center, renderer.CameraFraming.Center) > .001f ||
                    Math.Abs(lockedFraming.Distance - renderer.CameraFraming.Distance) > .001f))
                {
                    failed = true;
                    throw new InvalidOperationException("Camera changed scale or target during animation");
                }
                renderer.Orbit(6);
                var edgePixels = 0;
                const int size = ChickRenderer.Resolution;
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        if (x >= 4 && x < size - 4 && y >= 4 && y < size - 4) continue;
                        if (pixels[(y * size + x) * 4 + 3] > 48) edgePixels++;
                    }
                void Save(string suffix)
                {
                    var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"{models[model]}-{actions[action]}-{suffix}.png"));
                    png.Save(stream);
                }
                if (edgePixels > 2)
                {
                    if (++clipped == 1) Save("clipped");
                    failed = true;
                }
                var duration = PetActions.Loops(actions[action]) ? 3 : Math.Max(3, renderer.ActionDurations.GetValueOrDefault(actions[action]) + .3);
                if (clock.Elapsed.TotalSeconds - started < duration) return;
                Save("final");
                Console.WriteLine($"{models[model]} {actions[action]}: {frames} frames, clipped={clipped}");
                if (++action < actions.Count) BeginAction();
                else if (++model < models.Length) renderer.SelectAppearance(models[model]);
                else renderer.Dispose();
            }
            catch (Exception ex) { failed = true; Console.Error.WriteLine(ex); renderer.Dispose(); }
            finally { renderer.FrameConsumed(); }
        };
        renderer.SelectAppearance(models[0]);
        renderer.RunOnCurrentThread();
        Console.WriteLine($"Checked {total} frames");
        return failed || model != models.Length ? 1 : 0;
    }
}
