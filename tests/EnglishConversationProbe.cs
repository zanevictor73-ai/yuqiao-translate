using System;
using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Yuqiao;

class EnglishConversationProbe {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int count;static string expected;
 static object Get(object value,string name){return value.GetType().GetField(name,Flags).GetValue(value);}
 static void Set(object value,string name,object v){value.GetType().GetField(name,Flags).SetValue(value,v);}
 static object Call(object value,string name,params object[] args){return value.GetType().GetMethod(name,Flags).Invoke(value,args);}
 static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);count++;Console.WriteLine("PASS: "+message);}
 static string Hash(string path){using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(file));}
 static void Pump(Task task){var until=DateTime.UtcNow.AddSeconds(12);while(!task.IsCompleted){if(DateTime.UtcNow>until)throw new Exception("timeout");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(5);}task.GetAwaiter().GetResult();}
 sealed class EnglishHandler:HttpMessageHandler {
  public string LastIncomingSource="";public bool SlowEnglish;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   string query=request.RequestUri.Query;bool japanese=query.Contains("to=ja")||query.Contains("tl=ja");bool incoming=query.Contains("to=zh-Hans")||query.Contains("tl=zh-CN");
   if(incoming)LastIncomingSource=query.Contains("from=en")?"en":query.Contains("from=ja")?"ja":"auto";
   await Task.Delay(japanese?350:SlowEnglish?200:15,token);
   if(japanese)return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("unavailable")};
   string text=incoming?"京都以历史悠久的寺庙而闻名。":"Please introduce Kyoto in three short points.";
   return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[{\"translations\":[{\"text\":\""+text+"\"}]}]")};
  }
 }
 static Lane Ready(BridgeWindow w,string chinese,string english,string japanese){
  var lane=(Lane)Get(w,"outgoing");lane.Source.Text=chinese;lane.Done=lane.Seen;lane.First.Text=english;lane.Second.Text=japanese;lane.Cancel=new CancellationTokenSource();
  Set(w,"connected",true);Set(w,"boundConversationKey","claude|123|https://claude.ai/new");return lane;
 }
 static ChatSnapshot Snapshot(string draft){return new ChatSnapshot{Connected=true,ConversationKey="claude|123|https://claude.ai/new",Draft=draft};}
 [STAThread] static int Main(string[] args){expected=Path.GetFullPath(args[0]);string name=AssemblyName.GetAssemblyName(expected).Name;AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>new AssemblyName(e.Name).Name==name?Assembly.LoadFrom(expected):null;Assembly.LoadFrom(expected);SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());return Run();}
 static int Run(){try{
  Check(Hash(typeof(BridgeWindow).Assembly.Location)==Hash(expected),"English checks load the exact product binary");
  Check(ConversationLanguages.ReplySource("Kyoto has beautiful temples.",false)=="en","English replies are detected without Japanese kana");
  Check(ConversationLanguages.ReplySource("京都は歴史ある街です。",false)=="ja","Japanese replies remain supported");
  Check(ConversationLanguages.ReplySource("Use JavaScript: 日本語です。",false)=="ja","mixed Japanese reply prioritizes kana");
  Check(ConversationLanguages.ReplySource("这是中文回复，其中提到了 Claude。",false)==null,"Chinese with a product name is not mistaken for English");
  Check(ConversationLanguages.ReplySource("京都",true)=="ja","explicit kanji-only Japanese option remains available");
  Check(ConversationLanguages.ReplySource("12345",false)==null,"numeric-only text does not trigger translation");
  var w=new BridgeWindow(new[]{"--test-window","123"});
  Check((string)Get(w,"conversationLanguage")=="ja","new installations retain Japanese as the default");
  var selector=(ComboBox)Get(w,"languageSelector");selector.SelectedIndex=1;
  Check((string)Get(w,"conversationLanguage")=="en","visible language selector changes the outgoing target");
  string chinese="请用三个简短的要点介绍京都。",english="Please introduce Kyoto in three short points.";
  var lane=Ready(w,chinese,english,"");int writes=0,sends=0;string written="",previous=null;
  Set(w,"automaticComposer",new Action<string,string,string,CancellationToken>((key,text,old,token)=>{token.ThrowIfCancellationRequested();writes++;written=text;previous=old;}));
  Set(w,"explicitSender",new Action<string,string,CancellationToken>((key,text,token)=>{token.ThrowIfCancellationRequested();sends++;written=text;}));
  lane.Pending=new TaskCompletionSource<int>().Task;
  Pump((Task)Call(w,"TryAutomaticFill",Snapshot("")));
  Check(writes==1&&written==english&&previous=="","English automatically fills without clicking Fill");
  Check(!lane.Pending.IsCompleted,"English fill does not wait for pending Japanese translation");
  Check(sends==0,"automatic English fill never sends a message");
  Pump((Task)Call(w,"TryAutomaticFill",Snapshot(written)));
  Check(writes==1,"unchanged English is not repeatedly filled");
  Call(w,"RequestSend");Pump((Task)Call(w,"TryRequestedSend",Snapshot(written)));
  Check(sends==1&&written==english,"explicit Enter sends the selected English draft");
  var receipt=Snapshot("");receipt.UserMessages.Add(new ChatUserMessage{Id="u1",Text=english,Order=1});Call(w,"ObserveConversation",receipt);
  Check(lane.Source.Text=="","English send receipt clears the local Chinese draft");
  Check(((ConversationHistory)Get(w,"conversationHistory")).Render(receipt.ConversationKey).Contains(chinese),"English conversation history retains the Chinese original");
  Ready(w,"下一句话。","Next sentence.","次の文。");int before=writes;
  Pump((Task)Call(w,"TryAutomaticFill",Snapshot("My own manual draft.")));
  Check(writes==before,"English automation preserves an unrelated Claude draft");
  Call(w,"RequestSend");Check((int)Get(w,"requestedSendVersion")>=0,"a pending Enter is recorded for the current language");
  selector.SelectedIndex=0;
  Check((int)Get(w,"requestedSendVersion")==-1&&lane.Source.Text=="下一句话。","language switch cancels pending send while keeping Chinese");
  Check(lane.First.Text==""&&lane.Second.Text=="","language switch invalidates stale candidates");
  selector.SelectedIndex=1;
  var oldService=(TranslationService)Get(w,"service");oldService.Dispose();var handler=new EnglishHandler();
  var service=(TranslationService)typeof(TranslationService).GetConstructor(Flags,null,new[]{typeof(HttpMessageHandler)},null).Invoke(new object[]{handler});Set(w,"service",service);
  lane=Ready(w,"英文完成即可填入。","","");
  var translating=(Task)Call(w,"RunTranslation",lane,true,lane.Seen,lane.Version,(int)Get(w,"epoch"),DateTime.UtcNow,CancellationToken.None);
  Pump(Task.Delay(100));Check(lane.First.Text==english&&!translating.IsCompleted,"English becomes available before the failing Japanese request completes");
  Check((bool)Call(w,"CanFillTranslation"),"English is eligible independently of Japanese success");
  Pump(translating);Check(lane.First.Text==english&&lane.Second.Text=="","Japanese failure does not erase successful English");
  var incoming=(Lane)Get(w,"incoming");Call(w,"Observe",incoming,"Kyoto has historic temples.",true,"answer-en");incoming.Changed=DateTime.UtcNow.AddSeconds(-2);
  Pump((Task)Call(w,"Schedule",incoming,false));Pump(incoming.Pending);
  Check(handler.LastIncomingSource=="en"&&incoming.First.Text.Contains("京都"),"English replies are translated to Chinese by the live scheduling path");
  handler.SlowEnglish=true;lane=Ready(w,"切换前草稿。","","");var stale=(Task)Call(w,"RunTranslation",lane,true,lane.Seen,lane.Version,(int)Get(w,"epoch"),DateTime.UtcNow,CancellationToken.None);selector.SelectedIndex=0;Pump(stale);
  Check(lane.First.Text==""&&lane.Second.Text=="","late result from the previous language cannot return after switching");
  var parse=typeof(AppPreferences).GetMethod("ParseJson",BindingFlags.Static|BindingFlags.NonPublic);
  var preferences=(AppPreferences)parse.Invoke(null,new object[]{"{\"SelectedAppId\":\"claude\",\"ConversationLanguage\":\"en\"}"});
  Check(preferences.ConversationLanguage=="en","English preference is restored from settings");
  preferences=(AppPreferences)parse.Invoke(null,new object[]{"{\"ConversationLanguage\":\"unknown\"}"});
  Check(preferences.ConversationLanguage=="ja","invalid saved language safely defaults to Japanese");
  var floating=new FloatingInputWindow();Check(floating.LanguageSelector.Items.Count==2,"floating input offers both target languages");floating.ClosePermanently();
  Check(!w.IsVisible,"English regression checks used no real writer or microphone");w.Close();
  Console.WriteLine("ALL "+count+" CHECKS PASSED");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
