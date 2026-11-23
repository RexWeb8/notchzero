using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NotchZero
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            using var mutex = new Mutex(true, @"Local\NotchZero.SingleInstance", out bool isFirst);
            if (!isFirst) return;

            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error(e.ExceptionObject as Exception, "Unhandled");
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error(e.Exception, "Unobserved task");
                e.SetObserved();
            };

            System.Windows.Forms.Application.EnableVisualStyles();

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Error(e.Exception, "Dispatcher");
                e.Handled = true;
            };

            NotchController controller = null;
            app.Startup += (s, e) =>
            {
                Log.Info("NotchZero starting");
                controller = new NotchController();
                controller.Start();
            };
            app.Exit += (s, e) => controller?.Dispose();
            app.Run();
            GC.KeepAlive(mutex);
        }
    }

    /// <summary>Tiny file log in %LOCALAPPDATA%\NotchZero\log.txt, for troubleshooting.</summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchZero");

        public static void Info(string message) => Write("INFO", message);

        public static void Error(Exception ex, string context = null) =>
            Write("ERROR", (context != null ? context + ": " : "") + (ex?.ToString() ?? "(null)"));

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    var path = Path.Combine(Dir, "log.txt");
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > 512 * 1024) info.Delete();
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
