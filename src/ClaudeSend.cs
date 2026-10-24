using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Yuqiao {
 // Invoked only by an explicit Enter in the Chinese editor. Translation and
 // automatic filling never call this module. It never synthesizes Return.
 public static class ClaudeSend {
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
  [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
  public static bool IsEnterHeld(){return (GetAsyncKeyState(0x0D)&0x8000)!=0;}
  public static bool IsInteractionForeground(string key){
   string[] parts=(key??"").Split('|');long handle;if(parts.Length!=3||!Int64.TryParse(parts[1],out handle))return false;
   IntPtr foreground=GetForegroundWindow();if(foreground==new IntPtr(handle))return true;
   uint process;GetWindowThreadProcessId(foreground,out process);
   return process==(uint)Process.GetCurrentProcess().Id||WindowsVoiceInput.IsSystemInputForeground();
  }
  public static void FocusComposer(string key){
   if(!IsInteractionForeground(key)||WindowsVoiceInput.IsSystemInputForeground()||IsEnterHeld())return;
   try{var target=ClaudeTarget.FindUnique(key);var composer=target.FindVisibleComposer();
    target.EnsureConversation(key);if(!IsInteractionForeground(key))return;
    if(SetForegroundWindow(target.Window)){
     composer.SetFocus();
     object pattern;
     try{if(composer.TryGetCurrentPattern(TextPattern.Pattern,out pattern)){
      var end=((TextPattern)pattern).DocumentRange.Clone();
      end.MoveEndpointByRange(TextPatternRangeEndpoint.Start,end,TextPatternRangeEndpoint.End);
      end.Select();
     }}catch(InvalidOperationException){}catch(COMException){}
    }
   }catch(InvalidOperationException){}catch(ElementNotAvailableException){}catch(COMException){}
  }
  public static void Invoke(string key,string japanese,CancellationToken cancellation){
   if(String.IsNullOrWhiteSpace(japanese))throw new InvalidOperationException("没有可发送的日文。");
   cancellation.ThrowIfCancellationRequested();
   var target=ClaudeTarget.FindUnique(key);var composer=target.FindVisibleComposer();object supported;
   if(!composer.TryGetCurrentPattern(ValuePattern.Pattern,out supported))throw new InvalidOperationException("无法核对 Claude 草稿，已取消发送。");
   var value=(ValuePattern)supported;
   AutomationElement send=null;int count=0;
   foreach(AutomationElement button in target.Document.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button))){
    string name=button.Current.Name??"";
    if(!button.Current.IsOffscreen&&(name=="メッセージを送信"||name=="Send message"||name=="发送消息"||name=="傳送訊息")) {send=button;count++;}
   }
   if(count!=1||send==null||!send.Current.IsEnabled||!send.TryGetCurrentPattern(InvokePattern.Pattern,out supported))throw new InvalidOperationException("Claude 当前无法发送；请等待回复结束或关闭弹窗，中文已保留。");
   var invoke=(InvokePattern)supported;
   cancellation.ThrowIfCancellationRequested();target.EnsureConversation(key);
   if(ClaudeTarget.CleanDraft(value.Current.Value,composer.Current.Name)!=japanese.Trim())throw new InvalidOperationException("Claude 草稿已变化，已取消本次回车发送。请核对后再发送。");
   if(!IsInteractionForeground(key))throw new InvalidOperationException("已切换到其他软件，本次发送已取消。");
   cancellation.ThrowIfCancellationRequested();invoke.Invoke();
  }
 }
}
