using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ChickenDesktopPet3D;

internal static class UiLiveRegression
{
    public static int Run(string? output)
    {
        var directory = Path.GetFullPath(output ?? "research/panorama-live");
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("CHICK_SETTINGS_PATH", Path.Combine(directory, "settings.json"));
        Environment.SetEnvironmentVariable("CHICK_PHOTO_PATH", Path.Combine(directory, "photos"));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var renderer = new ChickRenderer();
        PetWindow? desktop = null;
        InspectWindow? inspect = null;
        Exception? error = null;
        var ready = new ManualResetEventSlim();
        var step = 0; var frames = 0; var total = 0;
        var awaiting = false;
        string CurrentModel() => ((PetAppearance?)typeof(PetWindow).GetField("selectedAppearance", flags)!.GetValue(desktop))?.Id ?? "";
        void Fail(Exception ex)
        {
            error = ex; Console.Error.WriteLine(ex); renderer.Dispose();
            desktop?.Dispatcher.BeginInvoke(() => desktop.Close(), DispatcherPriority.Send);
        }
        var ui = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                desktop = new PetWindow(renderer) { ShowActivated = false };
                app.MainWindow = desktop; desktop.Show(); desktop.Hide();
                ready.Set(); app.Run();
            }
            catch (Exception ex) { Fail(ex); ready.Set(); }
        });
        ui.SetApartmentState(ApartmentState.STA); ui.Start(); ready.Wait();
        using var timeout = new System.Threading.Timer(_ => Fail(new TimeoutException("Live UI timeout")), null,
            TimeSpan.FromSeconds(90), System.Threading.Timeout.InfiniteTimeSpan);
        renderer.Failed += Fail;
        renderer.AppearanceFailed += (_, ex) => Fail(ex);
        renderer.FrameReady += _ =>
        {
            if (++total < 35 || ++frames < 15 || awaiting || error is not null) return;
            awaiting = true;
            desktop!.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(async () =>
            {
                try
                {
                    object? Field(object target, string name) => target.GetType().GetField(name, flags)!.GetValue(target);
                    void Open()
                    {
                        typeof(PetWindow).GetMethod("OpenInspector", flags)!.Invoke(desktop, new object[] { false });
                        inspect = (InspectWindow)Field(desktop, "inspector")!; inspect.Opacity = 0;
                    }
                    switch (step)
                    {
                        case 0:
                            Open();
                            if (desktop.Opacity != 0 || !renderer.InspectionMode) throw new Exception("Desktop did not pause for inspection");
                            ((TextBox)Field(inspect!, "nameEntry")!).Text = "测试小鸡";
                            ((Button)Field(inspect!, "saveName")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (!inspect!.Title.StartsWith("测试小鸡")) throw new Exception("Name was not applied");
                            var models = (ComboBox)Field(inspect, "models")!;
                            models.SelectedItem = models.Items.Cast<PetAppearance>().First(item => item.ModelId == "chicken");
                            break;
                        case 1:
                            if (CurrentModel() != "chicken") { frames = 0; return; }
                            var skins = (ComboBox)Field(inspect!, "skins")!;
                            var colored = skins.Items.Cast<PetAppearance>().FirstOrDefault(item => item.Skin is not null);
                            if (colored is not null) skins.SelectedItem = colored;
                            break;
                        case 2:
                            if (CurrentModel() == "chicken") { frames = 0; return; }
                            typeof(InspectWindow).GetMethod("SetZoom", flags)!.Invoke(inspect, new object[] { 1.2f });
                            break;
                        case 3:
                            if (renderer.CameraFraming.Distance <= 0) throw new Exception("Invalid inspection camera");
                            var choices = (ComboBox)Field(inspect!, "models")!;
                            choices.SelectedItem = choices.Items.Cast<PetAppearance>().First(item => item.ModelId == "chick");
                            break;
                        case 4:
                            if (CurrentModel() != "chick") { frames = 0; return; }
                            if (!inspect!.Title.StartsWith("测试小鸡")) throw new Exception("Per-pet name was lost after switching");
                            typeof(InspectWindow).GetMethod("SetZoom", flags)!.Invoke(inspect, new object[] { 1f });
                            break;
                        case 5:
                            inspect!.UpdateLayout();
                            var content = (FrameworkElement)inspect.Content;
                            var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                            image.Render(content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                            using (var stream = File.Create(Path.Combine(directory, "inspector-live.png"))) png.Save(stream);
                            await inspect.TakePhotoAsync();
                            if (!Directory.EnumerateFiles(Path.Combine(directory, "photos"), "*.png").Any()) throw new Exception("Live photo was not saved");
                            inspect.Close();
                            if (desktop.Opacity != 1 || renderer.InspectionMode) throw new Exception("Desktop did not resume");
                            break;
                        case 6:
                            Open();
                            if (!inspect!.Title.StartsWith("测试小鸡")) throw new Exception("Reopened inspector lost name");
                            inspect.Close();
                            using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "settings.json"))))
                                if (json.RootElement.GetProperty("PetNames").GetProperty("chick").GetString() != "测试小鸡") throw new Exception("Name was not persisted");
                            desktop.Close();
                            break;
                    }
                    Console.WriteLine($"Live UI step {step}: PASS"); step++; frames = 0;
                }
                catch (Exception ex) { Fail(ex); }
                finally { awaiting = false; }
            }));
        };
        renderer.RunOnCurrentThread();
        desktop?.Dispatcher.BeginInvoke(() => desktop.Close(), DispatcherPriority.Send);
        ui.Join();
        Console.WriteLine($"Live UI frames: {total}");
        return error is null && step == 7 ? 0 : 1;
    }
}
