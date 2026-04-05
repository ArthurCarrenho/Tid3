using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace TidalUi3.Helpers;

public sealed class ThumbnailToolbar
{
    private const int THBF_ENABLED = 0x0;
    private const int THB_BITMAP = 0x1;
    private const int THB_FLAGS = 0x8;
    private const int THBN_CLICKED = 0x1800;

    private const int WM_COMMAND = 0x0111;

    // Button IDs
    public const int BtnPrevious = 0;
    public const int BtnPlayPause = 1;
    public const int BtnNext = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct THUMBBUTTON
    {
        public uint dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTip;
        public uint dwFlags;
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, int tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButtons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButtons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("comctl32.dll")]
    private static extern IntPtr ImageList_Create(int cx, int cy, uint flags, int cInitial, int cGrow);

    [DllImport("comctl32.dll")]
    private static extern int ImageList_ReplaceIcon(IntPtr himl, int i, IntPtr hicon);

    [DllImport("comctl32.dll")]
    private static extern bool ImageList_Destroy(IntPtr himl);

    [DllImport("shell32.dll")]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    private const uint ILC_COLOR32 = 0x20;
    private const uint ILC_MASK = 0x1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;

    private ITaskbarList3? _taskbar;
    private IntPtr _hwnd;
    private bool _buttonsAdded;
    private IntPtr _imageList;

    public event Action? PreviousClicked;
    public event Action? PlayPauseClicked;
    public event Action? NextClicked;

    public void Initialize(Microsoft.UI.Xaml.Window window)
    {
        _hwnd = WindowNative.GetWindowHandle(window);

        try
        {
            var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
            var iid = new Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");
            var hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, ref iid, out var obj);
            if (hr != 0 || obj == null) return;
            _taskbar = (ITaskbarList3)obj;
            _taskbar.HrInit();

            // Create image list with Segoe MDL2 icons rendered as bitmaps
            // We'll use icon handles from shell32 as simple fallback
            _imageList = ImageList_Create(20, 20, ILC_COLOR32 | ILC_MASK, 3, 0);

            // Load icons: use Segoe Fluent Icons via small rendered bitmaps
            var prevIcon = CreateIconFromGlyph("\uE892", 20); // Previous
            var playIcon = CreateIconFromGlyph("\uE768", 20); // Play
            var nextIcon = CreateIconFromGlyph("\uE893", 20); // Next

            ImageList_ReplaceIcon(_imageList, -1, prevIcon);
            ImageList_ReplaceIcon(_imageList, -1, playIcon);
            ImageList_ReplaceIcon(_imageList, -1, nextIcon);

            DestroyIcon(prevIcon);
            DestroyIcon(playIcon);
            DestroyIcon(nextIcon);

            _taskbar.ThumbBarSetImageList(_hwnd, _imageList);

            var buttons = new THUMBBUTTON[]
            {
                new() { dwMask = THB_BITMAP | THB_FLAGS, iId = BtnPrevious, iBitmap = 0, dwFlags = THBF_ENABLED, szTip = "Previous" },
                new() { dwMask = THB_BITMAP | THB_FLAGS, iId = BtnPlayPause, iBitmap = 1, dwFlags = THBF_ENABLED, szTip = "Play/Pause" },
                new() { dwMask = THB_BITMAP | THB_FLAGS, iId = BtnNext, iBitmap = 2, dwFlags = THBF_ENABLED, szTip = "Next" },
            };

            _taskbar.ThumbBarAddButtons(_hwnd, 3, buttons);
            _buttonsAdded = true;

            // Subclass the window to receive button click messages
            SetWindowSubclass(_hwnd, _subclassProc, 1, IntPtr.Zero);
        }
        catch
        {
            // Taskbar buttons are non-critical; silently fail
        }
    }

    public void UpdatePlayPauseIcon(bool isPlaying)
    {
        if (!_buttonsAdded || _taskbar == null) return;

        try
        {
            // Replace the play/pause icon in the image list
            var icon = CreateIconFromGlyph(isPlaying ? "\uE769" : "\uE768", 20); // Pause or Play
            ImageList_ReplaceIcon(_imageList, 1, icon);
            DestroyIcon(icon);
            _taskbar.ThumbBarSetImageList(_hwnd, _imageList);

            var buttons = new THUMBBUTTON[]
            {
                new() { dwMask = THB_BITMAP | THB_FLAGS, iId = BtnPlayPause, iBitmap = 1, dwFlags = THBF_ENABLED, szTip = isPlaying ? "Pause" : "Play" },
            };
            _taskbar.ThumbBarUpdateButtons(_hwnd, 1, buttons);
        }
        catch { }
    }

    private IntPtr CreateIconFromGlyph(string glyph, int size)
    {
        // Render a Segoe Fluent Icons glyph to an HICON using GDI+
        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Try Segoe Fluent Icons first (Win11), fall back to Segoe MDL2 Assets
            var fontFamily = System.Drawing.FontFamily.Families.Length > 0
                ? "Segoe Fluent Icons"
                : "Segoe MDL2 Assets";

            using var font = new System.Drawing.Font(fontFamily, size * 0.6f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

            var sf = new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
            };
            g.DrawString(glyph, font, brush, new System.Drawing.RectangleF(0, 0, size, size), sf);
        }
        return bmp.GetHicon();
    }

    // Window subclass to intercept thumbnail button clicks
    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    // Must be stored as a field to prevent GC
    private readonly SUBCLASSPROC _subclassProc;

    public ThumbnailToolbar()
    {
        _subclassProc = SubclassWndProc;
    }

    private IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == THBN_CLICKED)
        {
            var buttonId = wParam.ToInt32();
            switch (buttonId)
            {
                case BtnPrevious:
                    PreviousClicked?.Invoke();
                    return IntPtr.Zero;
                case BtnPlayPause:
                    PlayPauseClicked?.Invoke();
                    return IntPtr.Zero;
                case BtnNext:
                    NextClicked?.Invoke();
                    return IntPtr.Zero;
            }
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
