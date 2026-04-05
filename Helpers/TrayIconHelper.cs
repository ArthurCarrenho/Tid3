using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace TidalUi3.Helpers;

public sealed class TrayIconHelper : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_MESSAGE = 0x01;
    private const int NIM_ADD = 0x00;
    private const int NIM_DELETE = 0x02;

    private const int TPM_RIGHTALIGN = 0x08;
    private const int TPM_BOTTOMALIGN = 0x20;
    private const int TPM_RETURNCMD = 0x100;

    private const int MF_STRING = 0x0;
    private const int MF_SEPARATOR = 0x800;

    private const int CMD_SHOW = 1;
    private const int CMD_QUIT = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA pnid);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _iconAdded;
    private readonly SUBCLASSPROC _subclassProc;

    public event Action? ShowRequested;
    public event Action? QuitRequested;

    public TrayIconHelper()
    {
        _subclassProc = SubclassWndProc;
    }

    public void Show(Microsoft.UI.Xaml.Window window)
    {
        if (_iconAdded) return;

        _hwnd = WindowNative.GetWindowHandle(window);

        // Load the app icon
        _hIcon = LoadImage(IntPtr.Zero,
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Tid3.ico"),
            1 /* IMAGE_ICON */, 16, 16, 0x10 /* LR_LOADFROMFILE */);

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "Tid3"
        };

        Shell_NotifyIcon(NIM_ADD, ref nid);
        _iconAdded = true;

        SetWindowSubclass(_hwnd, _subclassProc, 2, IntPtr.Zero);
    }

    public void Remove()
    {
        if (!_iconAdded) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1
        };
        Shell_NotifyIcon(NIM_DELETE, ref nid);
        _iconAdded = false;

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    private IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_TRAYICON)
        {
            var msg = lParam.ToInt32() & 0xFFFF;
            if (msg == WM_LBUTTONDBLCLK)
            {
                ShowRequested?.Invoke();
                return IntPtr.Zero;
            }
            if (msg == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, CMD_SHOW, "Show Tid3");
        AppendMenu(hMenu, MF_SEPARATOR, 0, "");
        AppendMenu(hMenu, MF_STRING, CMD_QUIT, "Quit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);

        int cmd = TrackPopupMenu(hMenu, TPM_RIGHTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD,
            pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(hMenu);

        switch (cmd)
        {
            case CMD_SHOW:
                ShowRequested?.Invoke();
                break;
            case CMD_QUIT:
                QuitRequested?.Invoke();
                break;
        }
    }

    public void Dispose()
    {
        Remove();
        if (_hwnd != IntPtr.Zero)
            RemoveWindowSubclass(_hwnd, _subclassProc, 2);
    }
}
