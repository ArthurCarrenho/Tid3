using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace TidalUi3.Helpers;

public sealed class ThumbnailToolbar : IDisposable
{
    // Fraction of the icon box filled by the glyph. At 0.6 the icon-font strokes were
    // thin enough that antialiasing left no fully opaque pixels at all (mean alpha 86/255),
    // which is why the buttons looked washed out.
    private const float GlyphFillRatio = 0.8f;
    private const int THBF_ENABLED = 0x0;
    private const int THB_ICON = 0x2;
    private const int THB_TOOLTIP = 0x4;
    private const int THB_FLAGS = 0x8;
    private const int THBN_CLICKED = 0x1800;

    private const int WM_COMMAND = 0x0111;
    private const uint CLSCTX_INPROC_SERVER = 1;

    // Registered message id for "TaskbarButtonCreated"; 0 until Initialize runs.
    private uint _taskbarButtonCreatedMsg;

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
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private ITaskbarList3? _taskbar;
    private IntPtr _hwnd;
    private bool _buttonsAdded;

    // Button images are supplied as icon handles (THB_ICON) rather than through an image
    // list. The image list path rendered blank buttons: the glyphs draw correctly, but an
    // ILC_MASK list derives a 1-bit mask from the icon produced by Bitmap.GetHicon, which
    // wipes out an alpha-only image. The shell keeps using these handles for as long as
    // the buttons exist, so they are held here and destroyed in Dispose.
    private IntPtr _prevIcon;
    private IntPtr _playPauseIcon;
    private IntPtr _nextIcon;

    public event Action? PreviousClicked;
    public event Action? PlayPauseClicked;
    public event Action? NextClicked;


    public void Initialize(Microsoft.UI.Xaml.Window window)
    {
        _hwnd = WindowNative.GetWindowHandle(window);

        // The thumbnail toolbar can only be populated after the shell has created the
        // window's taskbar button. Doing it here (during window construction, before the
        // window is even shown) silently failed, which is why no buttons ever appeared.
        // The shell announces readiness with the registered TaskbarButtonCreated message,
        // and it is re-sent if Explorer restarts.
        _taskbarButtonCreatedMsg = RegisterWindowMessage("TaskbarButtonCreated");
        SetWindowSubclass(_hwnd, _subclassProc, 1, IntPtr.Zero);
    }


    // The shell wants a 16px button icon at 100% scaling. The old code always rendered
    // 20px, so on a high-DPI display the shell upscaled it, blurring strokes that were
    // already barely opaque.
    private int IconPixelSize()
    {
        var dpi = GetDpiForWindow(_hwnd);
        if (dpi == 0) dpi = 96;
        return (int)(16 * dpi / 96);
    }

    private void CreateToolbar()
    {
        try
        {
            var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
            var iid = new Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");

            // Returning the interface as IntPtr and unwrapping it by hand keeps this on
            // classic COM interop. Marshalling it straight into `out object` routed the
            // call through the WinRT projection, which failed with
            // "No COM servers are registered for this app" and left _taskbar null.
            var hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out var ptr);
            if (hr != 0 || ptr == IntPtr.Zero) return;

            try
            {
                _taskbar = (ITaskbarList3)Marshal.GetObjectForIUnknown(ptr);
            }
            finally
            {
                Marshal.Release(ptr);
            }

            _taskbar.HrInit();

            var iconSize = IconPixelSize();

            _prevIcon = CreateIconFromGlyph("\uE892", iconSize);      // Previous
            _playPauseIcon = CreateIconFromGlyph("\uE768", iconSize); // Play
            _nextIcon = CreateIconFromGlyph("\uE893", iconSize);      // Next

            if (_prevIcon == IntPtr.Zero || _playPauseIcon == IntPtr.Zero || _nextIcon == IntPtr.Zero)
                return;

            // THB_TOOLTIP has to be in the mask for szTip to be read at all; without it
            // the tooltips were being filled in and then ignored.
            var buttons = new THUMBBUTTON[]
            {
                new() { dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS, iId = BtnPrevious, hIcon = _prevIcon, dwFlags = THBF_ENABLED, szTip = "Previous" },
                new() { dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS, iId = BtnPlayPause, hIcon = _playPauseIcon, dwFlags = THBF_ENABLED, szTip = "Play/Pause" },
                new() { dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS, iId = BtnNext, hIcon = _nextIcon, dwFlags = THBF_ENABLED, szTip = "Next" },
            };

            _taskbar.ThumbBarAddButtons(_hwnd, 3, buttons);
            _buttonsAdded = true;
        }
        catch (Exception ex)
        {
            // Taskbar buttons are non-critical, but swallowing this silently is how the
            // COM failure above went unnoticed.
            System.Diagnostics.Debug.WriteLine($"ThumbnailToolbar init failed: {ex}");
        }
    }

    /// <summary>Releases the icon handles backing the taskbar buttons.</summary>
    public void Dispose()
    {
        foreach (var icon in new[] { _prevIcon, _playPauseIcon, _nextIcon })
        {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }
        _prevIcon = _playPauseIcon = _nextIcon = IntPtr.Zero;
    }

    public void UpdatePlayPauseIcon(bool isPlaying)
    {
        if (!_buttonsAdded || _taskbar == null) return;

        try
        {
            var icon = CreateIconFromGlyph(isPlaying ? "\uE769" : "\uE768", IconPixelSize()); // Pause or Play
            if (icon == IntPtr.Zero) return;

            var buttons = new THUMBBUTTON[]
            {
                new() { dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS, iId = BtnPlayPause, hIcon = icon, dwFlags = THBF_ENABLED, szTip = isPlaying ? "Pause" : "Play" },
            };
            _taskbar.ThumbBarUpdateButtons(_hwnd, 1, buttons);

            // Swap only after the shell has taken the new handle, so the button is never
            // pointing at a destroyed icon.
            var previous = _playPauseIcon;
            _playPauseIcon = icon;
            if (previous != IntPtr.Zero) DestroyIcon(previous);
        }
        catch { }
    }


    // Segoe Fluent Icons on Win11, Segoe MDL2 Assets on Win10. The previous check was
    // `FontFamily.Families.Length > 0`, which is always true, so it always asked for the
    // Win11 font; the Font(string, ...) constructor then silently substitutes a generic
    // family instead of throwing, which would draw blank glyphs on Win10.
    private static readonly string IconFontFamily = ResolveIconFontFamily();

    private static string ResolveIconFontFamily()
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try
            {
                using var family = new System.Drawing.FontFamily(name);
                return name;
            }
            catch (ArgumentException) { /* not installed */ }
        }
        return "Segoe UI Symbol";
    }

    private static IntPtr CreateIconFromGlyph(string glyph, int size)
    {
        // Render a Segoe Fluent Icons glyph to an HICON using GDI+
        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            // Plain AntiAlias, not AntiAliasGridFit: grid fitting snaps these thin glyph
            // strokes to the pixel grid and thins them further.
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            using var font = new System.Drawing.Font(IconFontFamily, size * GlyphFillRatio, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

            using var sf = new System.Drawing.StringFormat
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
    private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    // Must be stored as a field to prevent GC
    private readonly SUBCLASSPROC _subclassProc;

    public ThumbnailToolbar()
    {
        _subclassProc = SubclassWndProc;
    }

    private IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
    {
        if (_taskbarButtonCreatedMsg != 0 && uMsg == _taskbarButtonCreatedMsg)
        {
            CreateToolbar();
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        // The shell reports thumbnail button clicks as WM_COMMAND with THBN_CLICKED in the
        // high word of wParam and the button id in the low word. Matching uMsg against
        // THBN_CLICKED instead waits for a message the shell never sends, so the buttons
        // did nothing; wParam also has to be split rather than read whole.
        if (uMsg == WM_COMMAND && (int)((wParam.ToInt64() >> 16) & 0xFFFF) == THBN_CLICKED)
        {
            var buttonId = (int)(wParam.ToInt64() & 0xFFFF);
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
