using System;
using System.Runtime.InteropServices;

namespace NativeLib.Linux
{
    using Display = IntPtr;
    using Window = IntPtr;

    public static class XRandr
    {
        private const string libXRandr = "libXrandr";

        [DllImport(libXRandr, EntryPoint = "XRRGetMonitors")]
        public unsafe extern static XRRMonitorInfo* XRRGetMonitors(Display dpy, Window window, bool get_active, out int nmonitors);
    }
}