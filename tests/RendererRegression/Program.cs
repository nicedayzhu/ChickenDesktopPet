using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChickenDesktopPet3D;

internal static class Program
{
    // Integration test: requires the local CS2 VPK and an OpenGL-capable desktop.
    [STAThread]
    private static int Main(string[] args)
    {
        var directory = Path.GetFullPath(args.FirstOrDefault() ?? "research/switch-regression");
        Directory.CreateDirectory(directory);
        string[] sequence = ["chick", "chicken", "chick", "chicken_polish", "chick",
            "chicken_silkie", "chick", "chicken:1", "chicken:2", "chicken", "chick",
            "egg_pristine", "chick", "chicken_roasted", "chick"];
        using var renderer = new ChickRenderer();
        var step = 0;
        var frames = 0;
        var failed = false;
        double baselineDark = 0, baselineLight = 0;
        using var timeout = new System.Threading.Timer(_ =>
        {
            failed = true;
            Console.Error.WriteLine("FAIL: renderer timed out");
            renderer.Dispose();
        }, null, TimeSpan.FromSeconds(90), System.Threading.Timeout.InfiniteTimeSpan);
        renderer.Failed += ex => { failed = true; Console.Error.WriteLine(ex); renderer.Dispose(); };
        renderer.AppearanceFailed += (id, ex) =>
        {
            failed = true; Console.Error.WriteLine($"FAIL {id}: {ex}"); renderer.Dispose();
        };
        renderer.AppearanceChanged += (appearance, _) =>
        {
            if (appearance.Id != sequence[step]) { failed = true; renderer.Dispose(); }
            frames = 0;
        };
        renderer.FrameReady += pixels =>
        {
            try
            {
                if (++frames != 45) return;
                var bitmap = BitmapSource.Create(ChickRenderer.Resolution, ChickRenderer.Resolution,
                    96, 96, PixelFormats.Pbgra32, null, pixels, ChickRenderer.Resolution * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(directory, $"{step:D2}-{sequence[step].Replace(':', '-')}.png")))
                    encoder.Save(stream);
                var opaque = 0;
                var dark = 0;
                double light = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] < 250) continue;
                    opaque++;
                    var sum = pixels[i] + pixels[i + 1] + pixels[i + 2];
                    light += sum / 3.0;
                    if (sum < 45) dark++;
                }
                var darkRatio = opaque == 0 ? 1 : dark / (double)opaque;
                var mean = light / Math.Max(1, opaque);
                Console.WriteLine($"{step:D2} {sequence[step]} dark={darkRatio:P2} mean={mean:F2}");
                if (step == 0) { baselineDark = darkRatio; baselineLight = mean; }
                else if (sequence[step] == "chick" &&
                    (opaque == 0 || darkRatio > baselineDark + .03 || Math.Abs(mean - baselineLight) > baselineLight * .15))
                {
                    failed = true;
                    Console.Error.WriteLine("FAIL: returned chick lighting differs from initial chick");
                }
                if (++step == sequence.Length) renderer.Dispose();
                else renderer.SelectAppearance(sequence[step]);
            }
            catch (Exception ex) { failed = true; Console.Error.WriteLine(ex); renderer.Dispose(); }
            finally { renderer.FrameConsumed(); }
        };
        renderer.SelectAppearance(sequence[0]);
        renderer.RunOnCurrentThread();
        return failed || step != sequence.Length ? 1 : 0;
    }
}
