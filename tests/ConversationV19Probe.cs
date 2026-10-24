using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Yuqiao;

class ConversationV19Probe {
 static int count;static string expected;
 const string Key="claude|123|https://claude.ai/chat/fixture";
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 sealed class FakeTranslation:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[{\"translations\":[{\"text\":\"模拟中文译文。\"}]}]")});}}
 static object Get(object o,string name){return o.GetType().GetField(name,Flags).GetValue(o);}
 static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}
 static object Call(object o,string name,params object[] args){try{return o.GetType().GetMethod(name,Flags).Invoke(o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static void Check(bool pass,string name){if(!pass)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
 static void Pump(Task task){DateTime until=DateTime.UtcNow.AddSeconds(6);while(!task.IsCompleted){if(DateTime.UtcNow>until)throw new TimeoutException();var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);Thread.Sleep(3);}task.GetAwaiter().GetResult();}
 static string Hash(string path){using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f));}
 static ChatSnapshot Snap(string draft,params string[] messages){var s=new ChatSnapshot{Connected=true,ConversationKey=Key,Draft=draft};for(int i=0;i<messages.Length;i++)s.UserMessages.Add(new ChatUserMessage{Id="u"+i,Text=messages[i],Order=i*2+1});return s;}
 static void Ready(BridgeWindow w,string chinese,string japanese){var lane=(Lane)Get(w,"outgoing");lane.Source.Text=chinese;lane.Done=lane.Seen;lane.Second.Text=japanese;lane.Cancel=new CancellationTokenSource();Set(w,"connected",true);Set(w,"boundConversationKey",Key);}
 [STAThread] static int Main(string[] args){expected=Path.GetFullPath(args[0]);string name=AssemblyName.GetAssemblyName(expected).Name;AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>new AssemblyName(e.Name).Name==name?Assembly.LoadFrom(expected):null;Assembly.LoadFrom(expected);SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());return Run();}
 static int Run(){try{
  Check(Hash(typeof(BridgeWindow).Assembly.Location)==Hash(expected),"exact built product assembly is loaded");
  var flow=new ConversationFlow();ChatUserMessage sent;
  flow.Written(Snap(""),Key,"第一句","最初の文。",1);
  Check(flow.Observe(Snap(""),out sent)==null,"manual deletion alone never clears Chinese");
  Check(flow.Observe(Snap("","別の文。"),out sent)==null,"unrelated posted text is not a send receipt");
  var other=Snap("","最初の文。");other.ConversationKey=Key+"other";
  Check(flow.Observe(other,out sent)==null,"different conversation cannot acknowledge a draft");
  var ack=flow.Observe(Snap("","最初の文。"),out sent);
  Check(ack!=null&&ack.Chinese=="第一句"&&sent.Id=="u0","new matching user bubble acknowledges sent draft");
  Check(flow.Observe(Snap("","最初の文。"),out sent)==null,"same sent bubble is acknowledged only once");
  flow.Written(Snap("","最初の文。"),Key,"第二次一样","最初の文。",2);
  Check(flow.Observe(Snap("","最初の文。"),out sent)==null,"existing identical message cannot clear another draft");
  Check(flow.Observe(Snap("","最初の文。","最初の文。"),out sent)!=null,"repeated identical question needs a new message");
  flow.Written(Snap(""),Key,"修改前中文","元の文。",3);
  flow.Observe(Snap("手動で直した文。"),out sent);
  ack=flow.Observe(Snap("","手動で直した文。"),out sent);
  Check(ack!=null&&ack.EditedInClaude,"direct Japanese edits retain a clearly labelled Chinese original");
  flow.Written(Snap(""),"new", "新会话", "こんにちは。",4);flow.Move("new",Key);
  Check(flow.Observe(Snap("","こんにちは。"),out sent)!=null,"first send survives new-to-chat URL migration");
  var history=new ConversationHistory();var m=new ChatUserMessage{Id="q",Order=1};
  history.Reply(Key,"a",2,"回答一");history.User(Key,m,"问题一",true,false);
  Check(history.Render(Key).IndexOf("问题一")<history.Render(Key).IndexOf("回答一"),"late Chinese original remains before its answer");
  history.User(Key,m,"错误的回译",false,false);history.Reply(Key,"a",2,"回答完整");
  Check(history.Render(Key).Contains("问题一")&&!history.Render(Key).Contains("错误的回译")&&!history.Render(Key).Contains("回答一"),"original outranks back translation and streamed answer replaces its old text");
  Check(history.Render("other")=="","history does not leak across conversations");
  history.Move(Key,"moved");Check(history.Render("moved").Contains("问题一"),"history migrates with a newly created chat");
  var w=new BridgeWindow(new[]{"--test-window","123"});
  ((TranslationService)Get(w,"service")).Dispose();Set(w,"service",typeof(TranslationService).GetConstructor(Flags,null,new[]{typeof(HttpMessageHandler)},null).Invoke(new object[]{new FakeTranslation()}));
  var lane=(Lane)Get(w,"outgoing");int fills=0,sends=0,stops=0;
  Set(w,"automaticComposer",new Action<string,string,string,CancellationToken>((key,text,previous,token)=>{token.ThrowIfCancellationRequested();fills++;}));
  Set(w,"explicitSender",new Action<string,string,CancellationToken>((key,text,token)=>{token.ThrowIfCancellationRequested();sends++;}));
  Set(w,"systemVoiceEnder",new Action<TextBox>(box=>{stops++;}));
  Ready(w,"第一句中文。","最初の文。");
  Pump((Task)Call(w,"TryAutomaticFill",Snap("")));
  Check(fills==1&&sends==0,"translation alone only fills and never sends");
  Call(w,"ObserveConversation",Snap(""));Check(lane.Source.Text=="第一句中文。","empty target alone does not erase source in real UI state");
  Call(w,"RequestSend");Pump((Task)Call(w,"TryRequestedSend",Snap("最初の文。")));
  Check(sends==1&&lane.Source.Text=="第一句中文。","explicit Enter sends once but preserves Chinese until verified");
  Call(w,"RequestSend");Pump((Task)Call(w,"TryRequestedSend",Snap("最初の文。")));
  Check(sends==1,"repeated Enter during acknowledgement cannot double send");
  Set(w,"systemVoiceRequested",true);Call(w,"ObserveConversation",Snap("","最初の文。"));
  Check(lane.Source.Text==""&&lane.Second.Text==""&&!(bool)Get(w,"systemVoiceRequested"),"confirmed send clears stale text and resets voice for the next single click");
  Check(((ConversationHistory)Get(w,"conversationHistory")).Render(Key).Contains("第一句中文。"),"own Chinese is retained in reading history");
  Ready(w,"第二句中文。","次の文。");Set(w,"systemVoiceRequested",true);Call(w,"RequestSend");
  Pump((Task)Call(w,"TryRequestedSend",Snap("","最初の文。")));
  Check(stops==1&&sends==2&&!(bool)Get(w,"systemVoiceRequested"),"voice Enter ends native listening then sends the next question");
  Call(w,"ObserveConversation",Snap("","最初の文。","次の文。"));
  Check(lane.Source.Text==""&&((ConversationHistory)Get(w,"conversationHistory")).Render(Key).Contains("第二句中文。"),"two consecutive rounds never append the old Chinese");
  Ready(w,"待发送的一句。","送信を待つ文。");Call(w,"RequestSend");
  lane.Source.Text="回车后我又修改了中文。";Pump((Task)Call(w,"TryRequestedSend",Snap("")));
  Check(sends==2,"editing Chinese cancels a queued Enter");
  Ready(w,"旧中文原稿。","前の文。");var internalFlow=(ConversationFlow)Get(w,"conversationFlow");
  internalFlow.Written(Snap(""),Key,lane.Source.Text,"前の文。",lane.Version);lane.Source.Text="正在写的下一句。";
  Call(w,"ObserveConversation",Snap("","前の文。"));
  Check(lane.Source.Text=="正在写的下一句。","delayed acknowledgement cannot erase newly typed Chinese");
  Ready(w,"失败时保留。","失敗した文。");Set(w,"explicitSender",new Action<string,string,CancellationToken>((key,text,t)=>{throw new InvalidOperationException("模拟发送失败");}));
  Call(w,"RequestSend");Pump((Task)Call(w,"TryRequestedSend",Snap("")));
  Check(lane.Source.Text=="失败时保留。"&&((TextBlock)Get(w,"fillStatus")).Text.Contains("模拟发送失败"),"send failure preserves Chinese and reports a useful state");
  Set(w,"mainComposing",true);Call(w,"RequestSend");Check((int)Get(w,"requestedSendVersion")==-1,"IME composition cannot request sending");Set(w,"mainComposing",false);
  Call(w,"RequestSend");Set(w,"requestedSendAt",DateTime.UtcNow.AddSeconds(-30));Pump((Task)Call(w,"TryRequestedSend",Snap("")));
  Check((int)Get(w,"requestedSendVersion")==-1&&lane.Source.Text=="失败时保留。","expired Enter cannot send later unexpectedly");
  Call(w,"RequestSend");Call(w,"Pause","paused");Check((int)Get(w,"requestedSendVersion")==-1&&!(bool)Get(w,"awaitingSendReceipt"),"pause cancels pending commands");
  Call(w,"ShowFloatingMode");((DispatcherTimer)Get(w,"placementTimer")).Stop();
  var input=(FloatingInputWindow)Get(w,"floatInput");var read=(FloatingReadWindow)Get(w,"floatRead");var toggles=(FloatingPanelControls)Get(w,"panelControls");
  toggles.NavigationToggle.IsChecked=false;
  Check(((CheckBox)Get(w,"navigationEnabled")).IsChecked==false&&toggles.InputToggle.IsChecked==true&&toggles.ReadToggle.IsChecked==true,"menu can be hidden independently");
  toggles.InputToggle.IsChecked=false;toggles.ReadToggle.IsChecked=false;
  Check(((CheckBox)Get(w,"inputShown")).IsChecked==false&&((CheckBox)Get(w,"readingShown")).IsChecked==false,"all panels have independent off states");
  toggles.InputToggle.IsChecked=true;toggles.ReadToggle.IsChecked=true;
  Check(((CheckBox)Get(w,"inputShown")).IsChecked==true&&((CheckBox)Get(w,"readingShown")).IsChecked==true,"control strip restores hidden panels");
  input.Close();Check(((CheckBox)Get(w,"inputShown")).IsChecked==false&&((bool)Get(w,"floatingMode")),"closing a panel hides only that panel and keeps floating mode");
  Color background=((SolidColorBrush)input.Input.Background).Color,foreground=((SolidColorBrush)input.Input.Foreground).Color;
  Check(background.R<60&&background.G<60&&foreground.R>210,"input uses high-contrast dark colors");
  Check(((SolidColorBrush)read.Chinese.Background).Color.R<60,"reading panel uses the same dark surface");
  int keySends=0;var isolated=new FloatingInputWindow();isolated.SendRequested+=()=>keySends++;
  using(var source=new HwndSource(new HwndSourceParameters("invisible-key-fixture"){Width=0,Height=0,WindowStyle=0})){
   var enter=new KeyEventArgs(Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Return){RoutedEvent=Keyboard.PreviewKeyDownEvent};isolated.RaiseEvent(enter);
   Check(enter.Handled&&keySends==1,"actual floating key handler routes plain Enter to send request");
   TextComposition composition=new TextComposition(InputManager.Current,isolated.Input,"pin");isolated.Input.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,composition){RoutedEvent=TextCompositionManager.PreviewTextInputStartEvent});
   isolated.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Return){RoutedEvent=Keyboard.PreviewKeyDownEvent});
   Check(keySends==1,"actual key handler leaves IME candidate confirmation alone");
  }
  isolated.ClosePermanently();Check(!w.IsVisible&&!input.IsVisible&&!read.IsVisible&&!toggles.IsVisible,"all tests were invisible and used no real writer, microphone, or send");w.Close();
  Console.WriteLine("ALL "+count+" CHECKS PASSED");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
