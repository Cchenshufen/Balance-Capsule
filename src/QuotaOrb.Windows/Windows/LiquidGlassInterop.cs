using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuotaOrb.Windows.Windows;

internal static class LiquidGlassInterop
{
    private const int WindowCompositionAttributeAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;

    internal static void TryEnable(System.Windows.Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return;
        }

        var policy = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = unchecked((int)0x9AFFF8EE)
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
                new WindowInteropHelper(window).Handle,
                ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        nint windowHandle,
        ref WindowCompositionAttributeData data);

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
