using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DesktopWidgets;

/// <summary>
/// Widgets sit idle almost all the time, so after any burst of work (startup, wallpaper
/// rebuild, weather refresh) we compact the heap and hand unused pages back to Windows.
/// </summary>
internal static class MemoryTrim
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(5) };

    static MemoryTrim()
    {
        Timer.Tick += (_, _) =>
        {
            Timer.Stop();
            Trim();
        };
    }

    public static void Schedule()
    {
        Timer.Stop();
        Timer.Start();
    }

    private static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
