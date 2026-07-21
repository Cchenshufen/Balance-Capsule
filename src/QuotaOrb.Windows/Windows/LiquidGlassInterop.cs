using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuotaOrb.Windows.Windows;

internal static class LiquidGlassInterop
{
    private const int WindowCompositionAttributeAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int RegionOr = 2;

    internal static void TryEnable(
        System.Windows.Window window,
        bool clipToConnectedGlass = false)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (clipToConnectedGlass)
        {
            TryApplyConnectedGlassRegion(window, handle);
        }

        var policy = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = unchecked((int)0x42FFF8EE)
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttributeAccentPolicy,
                Data = pointer,
                SizeOfData = size
            };
            _ = SetWindowCompositionAttribute(
                handle,
                ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void TryApplyConnectedGlassRegion(
        System.Windows.Window window,
        nint windowHandle)
    {
        var dpi = GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var cardRegion = CreateRoundRectRgn(
            ScalePixel(25, dpi),
            ScalePixel(5, dpi),
            ScalePixel(window.Width - 5, dpi),
            ScalePixel(window.Height - 5, dpi),
            ScalePixel(44, dpi),
            ScalePixel(44, dpi));
        var neckRegion = CreateRoundRectRgn(
            ScalePixel(5, dpi),
            ScalePixel(73, dpi),
            ScalePixel(46, dpi),
            ScalePixel(125, dpi),
            ScalePixel(34, dpi),
            ScalePixel(34, dpi));
        var connectedRegion = CreateRectRgn(0, 0, 0, 0);

        if (cardRegion == 0 || neckRegion == 0 || connectedRegion == 0)
        {
            DeleteRegion(cardRegion);
            DeleteRegion(neckRegion);
            DeleteRegion(connectedRegion);
            return;
        }

        _ = CombineRgn(connectedRegion, cardRegion, neckRegion, RegionOr);
        DeleteRegion(cardRegion);
        DeleteRegion(neckRegion);

        if (SetWindowRgn(windowHandle, connectedRegion, true) == 0)
        {
            DeleteRegion(connectedRegion);
        }
    }

    private static int ScalePixel(double value, uint dpi) =>
        checked((int)Math.Round(value * dpi / 96d));

    private static void DeleteRegion(nint region)
    {
        if (region != 0)
        {
            _ = DeleteObject(region);
        }
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        nint windowHandle,
        ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(
        nint destination,
        nint sourceOne,
        nint sourceTwo,
        int combineMode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(
        nint windowHandle,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        internal int AccentState;
        internal int AccentFlags;
        internal int GradientColor;
        internal int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        internal int Attribute;
        internal nint Data;
        internal int SizeOfData;
    }
}
