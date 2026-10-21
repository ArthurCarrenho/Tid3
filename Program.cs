using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace TidalUi3;

/// <summary>
/// Entry point replacing the XAML-generated one (see DISABLE_XAML_GENERATED_MAIN in the
/// project) to keep Tid3 to a single instance. Launching it again while it runs, often hidden
/// in the tray, brings that window back instead of starting a second player that shares the
/// session files and fights the first one over the media keys.
/// </summary>
public static class Program
{
    // How long a second launch waits for the running instance to take over before giving up.
    private const uint RedirectTimeoutMs = 10_000;

    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectToRunningInstance())
            return;

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    /// <summary>True when an instance was already running and took over this launch.</summary>
    private static bool RedirectToRunningInstance()
    {
        var mainInstance = AppInstance.FindOrRegisterForKey("main");
        if (mainInstance.IsCurrent)
        {
            mainInstance.Activated += (_, _) =>
                App.MainWindow?.DispatcherQueue.TryEnqueue(() => App.MainWindow?.BringToFront());
            return false;
        }

        // A background process can't take the foreground by itself. This one was just started
        // by the user, so it may pass that right on to the instance that will show its window.
        AllowSetForegroundWindow(mainInstance.ProcessId);

        // Redirect on a worker thread and wait without blocking this STA's message pump, as the
        // Windows App SDK documents it: a plain blocking wait on this thread can deadlock.
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var redirected = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            try
            {
                mainInstance.RedirectActivationToAsync(activation).AsTask().Wait();
            }
            catch { /* the running instance is gone or hung; nothing more to do */ }
            finally
            {
                SetEvent(redirected);
            }
        });
        _ = CoWaitForMultipleObjects(0, RedirectTimeoutMs, 1, [redirected], out _);
        CloseHandle(redirected);
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    // cHandles is a ULONG (32-bit); declaring it 64-bit would corrupt the stack on x86.
    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(uint dwFlags, uint dwTimeout, uint cHandles, IntPtr[] pHandles, out uint lpdwindex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
