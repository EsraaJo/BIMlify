using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BIMlify.CleanerUi;

internal static class StartupDiagnostics
{
    private static readonly object FileGate = new object();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BIMlify", "Logs", "startup.log");
    private static bool _monitorStarted;
    private static int _sequence;

    internal static void StartUiResponsivenessMonitor()
    {
        if (_monitorStarted) return;
        _monitorStarted = true;

        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        Stopwatch clock = Stopwatch.StartNew();
        long previousTick = clock.ElapsedMilliseconds;
        int ticks = 0;
        timer.Tick += (sender, args) =>
        {
            long now = clock.ElapsedMilliseconds;
            long gap = now - previousTick;
            previousTick = now;
            ticks++;

            if (gap >= 250)
            {
                Write("ui-stall", gap + "ms");
            }

            if (ticks >= 80)
            {
                timer.Stop();
                timer.Dispose();
                _monitorStarted = false;
                Write("startup-monitor-ended", "");
            }
        };

        Write("startup-monitor-started", "");
        timer.Start();
    }

    internal static void RecordSplash(string phase, long durationMilliseconds = -1)
    {
        string detail = durationMilliseconds < 0 ? phase : phase + ";duration=" + durationMilliseconds + "ms";
        Write("welcome-splash", detail);
    }

    internal static void RecordSplashError(Exception exception)
    {
        Write("welcome-splash-error", exception == null ? "unknown" : exception.GetType().FullName);
    }

    private static void Write(string eventName, string detail)
    {
        int sequence = Interlocked.Increment(ref _sequence);
        string line = DateTime.UtcNow.ToString("o") + "\t" + sequence + "\t" + eventName + "\t" + detail + Environment.NewLine;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                string directory = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                lock (FileGate) File.AppendAllText(LogPath, line);
            }
            catch
            {
                // Diagnostics must never interfere with Revit or the add-in UI.
            }
        });
    }
}
