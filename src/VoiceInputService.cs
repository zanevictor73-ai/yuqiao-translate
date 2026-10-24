using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Yuqiao
{
    // Launch only. Windows owns recognition, microphone access and its UI.
    // No audio capture, recognition engine, recording file or cloud key here.
    public static class WindowsVoiceInput
    {
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public MOUSEINPUT mouse;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int x, y; public uint data, flags, time; public UIntPtr extra; }
        [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

        internal static INPUT[] Shortcut()
        { return new[] { Key(0x5B,false),Key(0x48,false),Key(0x48,true),Key(0x5B,true) }; }
        static INPUT Key(ushort key, bool up)
        { return new INPUT { type=1, data=new InputUnion { keyboard=new KEYBDINPUT { key=key,flags=up?2u:0u } } }; }
        public static void Request(TextBox editor)
        {
            if(editor==null||editor.IsReadOnly||!editor.IsEnabled||!editor.IsVisible)throw new InvalidOperationException("中文输入框当前不可用。");
            Window owner=Window.GetWindow(editor);
            if(owner==null||!owner.IsVisible||owner.WindowState==WindowState.Minimized)throw new InvalidOperationException("请先显示中文输入框。");
            if((GetAsyncKeyState(0x11)&0x8000)!=0||(GetAsyncKeyState(0x12)&0x8000)!=0||(GetAsyncKeyState(0x10)&0x8000)!=0)
                throw new InvalidOperationException("请先松开 Ctrl、Alt 和 Shift，再点击语音输入。");
            owner.Activate(); editor.Focus(); Keyboard.Focus(editor);
            editor.Select(editor.Text.Length,0);
            IntPtr handle=new WindowInteropHelper(owner).Handle;
            if(GetForegroundWindow()!=handle||!editor.IsKeyboardFocused)throw new InvalidOperationException("光标未能进入中文输入框，请再次点击语音输入。");
            INPUT[] inputs=Shortcut();
            uint sent=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(INPUT)));
            if(sent!=inputs.Length)
            {
                int error=Marshal.GetLastWin32Error();
                INPUT[] release={Key(0x48,true),Key(0x5B,true)};
                SendInput((uint)release.Length,release,Marshal.SizeOf(typeof(INPUT)));
                throw new Win32Exception(error,"系统未接受语音输入快捷键。");
            }
        }
        public static bool IsSystemInputForeground()
        {
            try
            {
                uint process; GetWindowThreadProcessId(GetForegroundWindow(),out process);
                using(Process p=Process.GetProcessById((int)process))
                    return String.Equals(p.ProcessName,"TextInputHost",StringComparison.OrdinalIgnoreCase)
                        ||String.Equals(p.ProcessName,"InputApp",StringComparison.OrdinalIgnoreCase);
            }catch(Exception){return false;}
        }
        public static void End(TextBox editor)
        {
            if(editor==null||!editor.IsVisible)throw new InvalidOperationException("请先显示中文输入框。");
            Window owner=Window.GetWindow(editor);
            if(owner==null)throw new InvalidOperationException("中文输入窗口不可用。");
            IntPtr handle=new WindowInteropHelper(owner).Handle;
            if(GetForegroundWindow()!=handle&&!IsSystemInputForeground())throw new InvalidOperationException("请在中文输入框结束听写。");
            owner.Activate();editor.Focus();Keyboard.Focus(editor);
            if(GetForegroundWindow()!=handle||!editor.IsKeyboardFocused)throw new InvalidOperationException("光标未在中文输入框，未切换到 Claude。");
            INPUT[] inputs={Key(0x1B,false),Key(0x1B,true)};
            if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(INPUT)))!=inputs.Length)throw new InvalidOperationException("系统未接受结束听写操作。");
        }
    }
}
