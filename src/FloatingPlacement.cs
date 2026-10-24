using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace Yuqiao
{
    public sealed class ClaudePlacement
    {
        public bool Available;
        public Rect InputBounds = Rect.Empty;
        public Rect WindowBounds = Rect.Empty;
        public Rect WorkArea = Rect.Empty;
        public bool TargetForeground;
    }

    // Position discovery only. No moving, sizing, showing, focusing or writing APIs.
    public static class FloatingPlacement
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        public static ClaudePlacement Read()
        {
            return Read(ReadCallerScale());
        }

        // Caller WPF coordinates determine the scale, independently of Claude's
        // monitor DPI. UI code should pass TransformToDevice.M11 for its own window.
        public static ClaudePlacement Read(double callerScale)
        {
            ClaudePlacement result = new ClaudePlacement();
            if (!IsValidScale(callerScale)) return result;
            try
            {
                IntPtr hwnd = IntPtr.Zero;
                int candidates = 0;
                foreach (Process process in Process.GetProcessesByName("claude"))
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        { hwnd = process.MainWindowHandle; candidates++; }
                    }
                    catch (InvalidOperationException) { }
                    finally { process.Dispose(); }
                }
                if (candidates != 1 || IsIconic(hwnd) || !IsWindowVisible(hwnd)) return result;
                AutomationElement window = AutomationElement.FromHandle(hwnd);
                AutomationElement document = FindClaudeDocument(window);
                if (document == null) return result;
                AutomationElement composer = FindVisibleComposer(document);
                if (composer == null) return result;
                Rect input = composer.Current.BoundingRectangle;
                if (input.IsEmpty || input.Width <= 0 || input.Height <= 0) return result;

                // Query after UIA initialization; that initialization can change the
                // caller's DPI-awareness state on the first .NET Framework call.
                NativeRect windowRectangle;
                if (!GetWindowRect(hwnd, out windowRectangle)
                    || windowRectangle.Right <= windowRectangle.Left || windowRectangle.Bottom <= windowRectangle.Top) return result;
                if (input.Left < windowRectangle.Left - 4 || input.Top < windowRectangle.Top - 4
                    || input.Right > windowRectangle.Right + 4 || input.Bottom > windowRectangle.Bottom + 4) return result;

                result.InputBounds = PhysicalToDip(input, callerScale);
                result.WindowBounds = PhysicalToDip(new Rect(windowRectangle.Left, windowRectangle.Top,
                    windowRectangle.Right - windowRectangle.Left, windowRectangle.Bottom - windowRectangle.Top), callerScale);
                result.WorkArea = PhysicalToDip(ReadPhysicalWorkArea(hwnd), callerScale);
                result.TargetForeground = IsClaudeOrSelfForeground();
                result.Available = true;
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (COMException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (ArgumentException) { }
            return result;
        }

        private static AutomationElement FindClaudeDocument(AutomationElement window)
        {
            AutomationElement result = null;
            int count = 0;
            AutomationElementCollection documents = window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            foreach (AutomationElement document in documents)
            {
                object supported;
                if (!document.TryGetCurrentPattern(ValuePattern.Pattern, out supported)) continue;
                Uri url;
                if (!Uri.TryCreate(((ValuePattern)supported).Current.Value, UriKind.Absolute, out url)) continue;
                if (url.Scheme != "https" || !string.Equals(url.Host, "claude.ai", StringComparison.OrdinalIgnoreCase)) continue;
                if (url.AbsolutePath != "/new" && !url.AbsolutePath.StartsWith("/chat/", StringComparison.Ordinal)) continue;
                count++;
                result = document;
            }
            return count == 1 ? result : null;
        }

        private static AutomationElement FindVisibleComposer(AutomationElement document)
        {
            AutomationElement selected = null;
            int count = 0;
            AutomationElementCollection edits = document.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            foreach (AutomationElement edit in edits)
            {
                if (edit.Current.IsOffscreen) continue;
                string[] tokens = (edit.Current.ClassName ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                bool composer = false;
                foreach (string token in tokens) if (token == "ProseMirror") { composer = true; break; }
                if (!composer) continue;
                selected = edit;
                count++;
            }
            return count == 1 ? selected : null;
        }

        private static double ReadCallerScale()
        {
            try
            {
                using (Process self = Process.GetCurrentProcess())
                {
                    IntPtr ownWindow = self.MainWindowHandle;
                    if (ownWindow != IntPtr.Zero)
                    {
                        uint dpi = GetDpiForWindow(ownWindow);
                        if (dpi >= 48 && dpi <= 768) return dpi / 96.0;
                    }
                }
            }
            catch (EntryPointNotFoundException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 48 && dpi <= 768) return dpi / 96.0;
            }
            catch (EntryPointNotFoundException) { }
            return 1.0;
        }

        private static bool IsValidScale(double scale)
        {
            return scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale);
        }

        private static Rect ReadPhysicalWorkArea(IntPtr hwnd)
        {
            const uint NearestMonitor = 2;
            IntPtr monitor = MonitorFromWindow(hwnd, NearestMonitor);
            if (monitor == IntPtr.Zero) return Rect.Empty;
            MonitorInfo information = new MonitorInfo();
            information.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(monitor, ref information)) return Rect.Empty;
            NativeRect work = information.Work;
            if (work.Right <= work.Left || work.Bottom <= work.Top) return Rect.Empty;
            return new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        }

        private static Rect PhysicalToDip(Rect rectangle, double scale)
        {
            if (rectangle.IsEmpty) return Rect.Empty;
            return new Rect(rectangle.Left / scale, rectangle.Top / scale,
                rectangle.Width / scale, rectangle.Height / scale);
        }

        private static bool IsClaudeOrSelfForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            uint processId;
            GetWindowThreadProcessId(foreground, out processId);
            using (Process self = Process.GetCurrentProcess())
                if (processId == (uint)self.Id) return true;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                    return string.Equals(process.ProcessName, "claude", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
    }
}
