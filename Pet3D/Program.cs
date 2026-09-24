using System.Windows;

namespace ChickenDesktopPet3D;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            using var renderer = new ChickRenderer();
            using var ready = new ManualResetEventSlim();
            Exception? uiError = null;
            var uiThread = new Thread(() =>
            {
                try
                {
                    var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    var window = new PetWindow(renderer);
                    ready.Set();
                    app.Run(window);
                }
                catch (Exception ex) { uiError = ex; ErrorLog.Write(ex); ready.Set(); }
                finally { renderer.Dispose(); }
            }) { Name = "CS2 chick desktop window" };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            ready.Wait();
            if (uiError is null) renderer.RunOnCurrentThread();
            uiThread.Join();
            if (uiError is not null) throw uiError;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            System.Windows.MessageBox.Show(ex.Message, "小鸡启动失败");
        }
    }
}

internal static class ErrorLog
{
    public static void Trace(string message)
    {
        if (Environment.GetEnvironmentVariable("CHICK_TRACE") is null) return;
        try
        {
            var path = Environment.GetEnvironmentVariable("CHICK_TRACE_PATH") ??
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChickenDesktopPet", "pet3d-debug.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch { }
    }

    public static void Write(Exception ex)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("CHICK_ERROR_PATH") ??
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChickenDesktopPet", "pet3d-error.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch { }
    }
}
