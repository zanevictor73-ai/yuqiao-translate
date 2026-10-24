using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Yuqiao {
 // Some web editors activate their host window while accepting ValuePattern.
 // During our own write, return only that specific activation to the local
 // editor. Never activate over a different application or after this scope.
 internal sealed class LocalInputFocusGuard : IDisposable {
  delegate void WinEvent(IntPtr hook,uint kind,IntPtr window,int obj,int child,uint thread,uint time);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
  [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
  readonly TextBox editor;readonly Window owner;readonly IntPtr target;readonly WinEvent callback;
  IntPtr hook;bool disposed;
  internal LocalInputFocusGuard(TextBox input,string conversationKey){
   editor=input;owner=input==null?null:Window.GetWindow(input);
   string[] parts=(conversationKey??"").Split('|');long handle;
   if(owner==null||!owner.IsVisible||owner.WindowState==WindowState.Minimized||!input.IsKeyboardFocused
      ||parts.Length<3||!Int64.TryParse(parts[1],out handle))return;
   IntPtr local=new WindowInteropHelper(owner).Handle;
   if(GetForegroundWindow()!=local&&!WindowsVoiceInput.IsSystemInputForeground())return;
   target=new IntPtr(handle);callback=(h,k,w,o,c,t,ms)=>{if(!disposed&&w==target)Restore();};
   hook=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
  }
  void Restore(){
   if(disposed||target==IntPtr.Zero||owner==null||!owner.IsVisible||owner.WindowState==WindowState.Minimized||GetForegroundWindow()!=target)return;
   owner.Activate();editor.Focus();Keyboard.Focus(editor);
  }
  public void Dispose(){if(disposed)return;Restore();disposed=true;if(hook!=IntPtr.Zero){UnhookWinEvent(hook);hook=IntPtr.Zero;}GC.KeepAlive(callback);}
 }
}
