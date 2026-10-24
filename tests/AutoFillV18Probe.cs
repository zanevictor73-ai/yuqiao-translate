using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Yuqiao;

class AutoFillV18Probe {
 static int count;
 static string expectedProduct;
 sealed class EnglishFailure : HttpMessageHandler {
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   bool japanese=request.RequestUri.Query.Contains("to=ja");await Task.Delay(japanese?25:450,token);
   return new HttpResponseMessage(japanese?HttpStatusCode.OK:HttpStatusCode.ServiceUnavailable){Content=new StringContent(japanese?"[{\"translations\":[{\"text\":\"日本語は成功。\"}]}]":"unavailable")};
  }
 }
 static string Hash(string file){using(var h=SHA256.Create())using(var f=File.OpenRead(file))return BitConverter.ToString(h.ComputeHash(f));}
 static readonly BindingFlags fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
 static object Get(object o,string n){return o.GetType().GetField(n,fields).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,fields).SetValue(o,v);}
 static void Pump(Task task){var until=DateTime.UtcNow.AddSeconds(8);while(!task.IsCompleted){if(DateTime.UtcNow>until)throw new Exception("test timed out");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(5);}task.GetAwaiter().GetResult();}
 static Task Fill(BridgeWindow w,string draft){return (Task)w.GetType().GetMethod("TryAutomaticFill",fields).Invoke(w,new object[]{new ChatSnapshot{Connected=true,ConversationKey="claude|123|https://claude.ai/new",Draft=draft}});}
 static void Ready(BridgeWindow w,string chinese,string japanese){var lane=(Lane)Get(w,"outgoing");lane.Source.Text=chinese;lane.Done=lane.Seen;lane.Second.Text=japanese;lane.Cancel=new CancellationTokenSource();lane.Pending=new TaskCompletionSource<int>().Task;Set(w,"connected",true);Set(w,"boundConversationKey","claude|123|https://claude.ai/new");}
 [STAThread] static int Main(string[] args){var file=Path.GetFullPath(args[0]);expectedProduct=file;string name=AssemblyName.GetAssemblyName(file).Name;AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>new AssemblyName(e.Name).Name==name?Assembly.LoadFrom(file):null;Assembly.LoadFrom(file);SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());return Run();}
 static int Run(){try{
  Check(Hash(typeof(BridgeWindow).Assembly.Location)==Hash(expectedProduct),"loaded assembly matches the exact release binary");
  string expected;var d=new AutomaticDraft();d.InputChanged();
  Check(d.TryPlan("a","","日本語。",out expected)&&expected=="","empty target can receive first automatic draft");
  d.RecordVerified("a","日本語。");
  Check(!d.TryPlan("a","日本語。","日本語。",out expected),"same result is not repeatedly filled");
  d.InputChanged();Check(d.TryPlan("a","日本語。","新しい文。",out expected)&&expected=="日本語。","new local input replaces only owned draft");
  d.RecordVerified("a","新しい文。");d.Observe("a","");
  Check(!d.TryPlan("a","","新しい文。",out expected),"cleared or sent draft is not reinserted");
  d.InputChanged();Check(d.TryPlan("a","","次の文。",out expected),"new local input resumes after clearing");
  d.RecordVerified("a","次の文。");d.Observe("a","手入力です。");
  Check(!d.TryPlan("a","手入力です。","上書き。",out expected),"manual target edit is preserved");
  d.InputChanged();Check(!d.TryPlan("a","手入力です。","上書き。",out expected),"new local input cannot take over unrelated draft");
  Check(d.TryPlan("a","","上書き。",out expected),"clearing an unrelated draft allows pending new text");
  d.RecordVerified("a","上書き。");d.Observe("b","");
  Check(!d.TryPlan("b","","上書き。",out expected),"navigation cannot reinsert old text in a new chat");
  d.InputChanged();Check(d.TryPlan("b","","別の文。",out expected),"new input can target newly connected chat");
  Check(!d.TryPlan("b","","別の文。",out expected),"unchanged failed attempt does not flood UI");
  d.Suspend();Check(!d.TryPlan("b","","別の文。",out expected),"reconnection alone does not reuse old input");
  var conflict=typeof(ClaudeComposer).GetMethod("RejectConflict",BindingFlags.Static|BindingFlags.NonPublic);
  conflict.Invoke(null,new object[]{"旧文。","新文。","旧文。"});Check(true,"composer accepts exact owned previous text");
  bool refused=false;try{conflict.Invoke(null,new object[]{"別の草稿。","新文。","旧文。"});}catch(TargetInvocationException e){refused=e.InnerException is InvalidOperationException;}
  Check(refused,"composer rejects changed draft immediately before writing");
  bool canceled=false;var c=new CancellationTokenSource();c.Cancel();try{ClaudeComposer.FillJapanese("claude","key","日本語。",null,c.Token);}catch(OperationCanceledException){canceled=true;}
  Check(canceled,"canceled fill exits before any desktop access");
  var w=new BridgeWindow(new[]{"--test-window","123"});
  Check(((CheckBox)Get(w,"autoFill")).IsChecked==true,"automatic fill is enabled by default");
  Ready(w,"明天晚上七点见。","明日の夜7時に会いましょう。");int calls=0;string written="",prior=null;
  Set(w,"automaticComposer",new Action<string,string,string,CancellationToken>((key,text,previous,token)=>{token.ThrowIfCancellationRequested();calls++;written=text;prior=previous;}));
  Pump(Fill(w,""));Check(calls==1&&written=="明日の夜7時に会いましょう。"&&prior=="","translation automatically reaches composer without Fill click");
  Check(!((Lane)Get(w,"outgoing")).Pending.IsCompleted,"Japanese fill does not wait for pending English translation");
  Pump(Fill(w,written));Check(calls==1,"polling does not duplicate automatic fill");
  string old=written;Ready(w,"改成明天晚上八点见。","明日の夜8時に変更しましょう。");Pump(Fill(w,old));Check(calls==2&&prior==old,"continuing Chinese updates previously filled Japanese");
  Pump(Fill(w,"手動の下書きです。"));Check(calls==2,"live pipeline preserves manually changed target draft");
  ((CheckBox)Get(w,"autoFill")).IsChecked=false;Ready(w,"你好。","こんにちは。");Pump(Fill(w,""));Check(calls==2,"off switch stops automatic writes");
  ((CheckBox)Get(w,"autoFill")).IsChecked=true;
  Set(w,"mainComposing",true);Pump(Fill(w,""));Check(calls==2,"unfinished Chinese IME composition is not filled");Set(w,"mainComposing",false);
  using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
   Set(w,"automaticComposer",new Action<string,string,string,CancellationToken>((key,text,previous,token)=>{entered.Set();release.WaitOne(3000);token.ThrowIfCancellationRequested();calls++;}));
   var task=Fill(w,"");Check(entered.WaitOne(3000),"automatic writer started asynchronously");
   var lane=(Lane)Get(w,"outgoing");Check(!lane.Source.IsReadOnly,"voice dictation stays editable while Japanese is filled");
   lane.Source.Text="又改了新的中文。";release.Set();Pump(task);Check(calls==2,"new Chinese cancels stale pending automatic write");
  }
  var service=(TranslationService)Get(w,"service");service.Dispose();
  var fake=(TranslationService)typeof(TranslationService).GetConstructor(fields,null,new[]{typeof(HttpMessageHandler)},null).Invoke(new object[]{new EnglishFailure()});Set(w,"service",fake);
  Ready(w,"英文失败时仍然翻译日文。","");var outgoing=(Lane)Get(w,"outgoing");
  var translating=(Task)w.GetType().GetMethod("RunTranslation",fields).Invoke(w,new object[]{outgoing,true,outgoing.Seen,outgoing.Version,(int)Get(w,"epoch"),DateTime.UtcNow,CancellationToken.None});outgoing.Pending=translating;
  Pump(Task.Delay(180));Check(outgoing.Second.Text=="日本語は成功。"&&!translating.IsCompleted,"Japanese becomes visible before slow English finishes");
  Check((bool)w.GetType().GetMethod("CanFillJapanese",fields).Invoke(w,null),"fresh Japanese remains eligible while English is pending");
  Pump(translating);Check(outgoing.First.Text==""&&outgoing.Second.Text=="日本語は成功。","English primary and backup failure cannot clear successful Japanese");
  Check(!w.IsVisible,"pipeline tests used no real desktop writer or microphone");w.Close();
  Console.WriteLine("ALL "+count+" CHECKS PASSED");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
