using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Rescate.Desktop;

internal static class WindowSizing
{
    // Work area excludes the taskbar. Convert physical pixels to WPF units.
    internal static Size FitSize(double width,double height,double workWidth,double workHeight,double scaleX,double scaleY)
        => new(Math.Min(width,workWidth/scaleX),Math.Min(height,workHeight/scaleY));

    internal static void FitRestoreBounds(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(handle,2),ref info))return;
        var dpi=VisualTreeHelper.GetDpi(window);
        var size=FitSize(window.Width,window.Height,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top,dpi.DpiScaleX,dpi.DpiScaleY);
        window.Width=size.Width;window.Height=size.Height;
    }
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
}
