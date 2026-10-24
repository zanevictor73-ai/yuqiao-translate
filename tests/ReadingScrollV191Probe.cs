using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Yuqiao;

class ReadingScrollV191Probe {
 static int count;
 static string expected;
 static void Check(bool value,string description){if(!value)throw new Exception("FAIL: "+description);count++;Console.WriteLine("PASS: "+description);}
 static string Hash(string path){using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f));}
 static void Pump(){for(int i=0;i<3;i++){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}}
 static string Answer(int lines){var s=new StringBuilder();for(int i=1;i<=lines;i++)s.AppendLine("回答第 "+i+" 行：这是仅用于检查滚动位置的本地示例内容。");return s.ToString();}
 sealed class Surface:IDisposable {
  public TextBox Box;public ConversationReadingPosition Position;readonly HwndSource source;
  public Surface(bool arrange){
   Box=new TextBox{Width=310,Height=195,FontSize=16,FontFamily=new FontFamily("Microsoft YaHei UI"),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Padding=new Thickness(8),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
   Position=new ConversationReadingPosition(Box);
   source=new HwndSource(new HwndSourceParameters("invisible-reading-layout-fixture"){WindowStyle=unchecked((int)0x80000000),Width=310,Height=195});
   if(arrange)Arrange();
  }
  public void Arrange(){source.RootVisual=Box;Box.Measure(new Size(310,195));Box.Arrange(new Rect(0,0,310,195));Box.UpdateLayout();Pump();}
  public void Update(string key,ConversationDocument doc){Position.Update(key,doc);Box.UpdateLayout();Pump();}
  public void Scroll(double offset){Box.ScrollToVerticalOffset(offset);Box.UpdateLayout();Pump();}
  public double Bottom(string text){int end=Box.Text.LastIndexOf(text,StringComparison.Ordinal)+text.Length-1;return Box.GetRectFromCharacterIndex(end,true).Bottom;}
  public double ReadingBottom{get{return Box.ActualHeight-Box.Padding.Bottom-Box.BorderThickness.Bottom;}}
  public void Dispose(){source.Dispose();}
 }
 [STAThread] static int Main(string[] args){try{
  expected=Path.GetFullPath(args[0]);var name=AssemblyName.GetAssemblyName(expected).Name;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>new AssemblyName(e.Name).Name==name?Assembly.LoadFrom(expected):null;Assembly.LoadFrom(expected);
  return Run();
 }catch(Exception e){Console.WriteLine(e);return 1;}}
 static int Run(){
  Check(Hash(typeof(ConversationReadingPosition).Assembly.Location)==Hash(expected),"exact product assembly is used for actual WPF layout checks");
  var history=new ConversationHistory();const string key="fixture-one";const string question="请把第一步解释清楚。";
  history.User(key,new ChatUserMessage{Id="q1",Order=1},question,true,false);
  var doc=history.Document(key);
  Check(doc.LastUserId=="u:q1"&&doc.Text.Substring(0,doc.LastUserEnd).EndsWith(question),"question anchor uses message identity and its own final character");
  using(var view=new Surface(true)){
   var focus=Keyboard.FocusedElement;
   view.Update(key,doc);
   Console.WriteLine("METRIC: first question bottom="+view.Bottom(question)+" viewport="+view.Box.ViewportHeight+" offset="+view.Box.VerticalOffset);
   Check(Math.Abs(view.Bottom(question)-view.ReadingBottom)<3,"even the first short question ends at the bottom of the reading area");
   Check(view.Box.CaretIndex==view.Box.Text.LastIndexOf(question,StringComparison.Ordinal)+question.Length,"later tab focus targets the question instead of the beginning of the document");
   double offset=view.Box.VerticalOffset;
   history.Reply(key,"a1",2,Answer(35));view.Update(key,history.Document(key));
   Check(Math.Abs(view.Box.VerticalOffset-offset)<1&&Math.Abs(view.Bottom(question)-view.ReadingBottom)<3,"first long answer remains below the question instead of jumping to its last line");
   Check(view.Box.CaretIndex==view.Box.Text.LastIndexOf(question,StringComparison.Ordinal)+question.Length,"an answer update preserves the caret used by tab focus");
   view.Scroll(320);offset=view.Box.VerticalOffset;
   Check(offset>200,"reader can scroll down into the answer");
   int selected=view.Box.Text.IndexOf("回答第 2 行",StringComparison.Ordinal);view.Box.Select(selected,5);view.Scroll(offset);
   history.Reply(key,"a1",2,Answer(55));view.Update(key,history.Document(key));
   Check(Math.Abs(view.Box.VerticalOffset-offset)<1,"streaming translation preserves a manually chosen reading position");
   Check(view.Box.SelectionStart==selected&&view.Box.SelectionLength==5,"reading updates preserve selected text without moving the viewport to the selection");
   view.Scroll(double.MaxValue);offset=view.Box.VerticalOffset;
   history.Reply(key,"a1",2,Answer(75));view.Update(key,history.Document(key));
   Check(Math.Abs(view.Box.VerticalOffset-offset)<1&&view.Box.ExtentHeight-view.Box.ViewportHeight-view.Box.VerticalOffset>100,"being at the old bottom does not follow new answer text");
   view.Scroll(80);offset=view.Box.VerticalOffset;
   for(int i=0;i<5;i++){history.Reply(key,"a1",2,Answer(80+i));view.Update(key,history.Document(key));}
   Check(Math.Abs(view.Box.VerticalOffset-offset)<1,"repeated answer updates do not pull the reader away from earlier lines");
   const string next="那么第二步应该怎么做？";
   history.User(key,new ChatUserMessage{Id="q2",Order=3},next,true,false);view.Update(key,history.Document(key));
   Check(Math.Abs(view.Bottom(next)-view.ReadingBottom)<3,"the next question positions its own final line at the bottom");
   history.Reply(key,"a2",4,Answer(28));view.Update(key,history.Document(key));
   Check(Math.Abs(view.Bottom(next)-view.ReadingBottom)<3,"the second answer also stays below its question");
   history.User(key,new ChatUserMessage{Id="q3",Order=5},next,true,false);view.Update(key,history.Document(key));
   Check(Math.Abs(view.Bottom(next)-view.ReadingBottom)<3,"identical text sent again is recognized as a new question");
   history.Reply(key,"a3",6,Answer(26));view.Update(key,history.Document(key));view.Scroll(view.Box.VerticalOffset+110);offset=view.Box.VerticalOffset;
   history.Move(key,"moved");view.Position.Move(key,"moved");view.Update("moved",history.Document("moved"));
   Check(Math.Abs(view.Box.VerticalOffset-offset)<1,"new-chat URL migration preserves the current reading position");
   history.Reply("other","a1",2,Answer(40));view.Update("other",history.Document("other"));
   Check(view.Box.VerticalOffset<1,"an answer without a known question initially starts at its beginning");
   history.User("other",new ChatUserMessage{Id="late",Order=1},question,false,false);view.Update("other",history.Document("other"));
   Check(Math.Abs(view.Bottom(question)-view.ReadingBottom)<3,"late user translation anchors the correct question before an existing answer");
   Check(Keyboard.FocusedElement==focus,"reading updates never take the keyboard focus");
  }
  using(var hidden=new Surface(false)){
   hidden.Update("hidden",new ConversationDocument{Text="我 · 中文原稿\n"+question,LastUserId="u:late",LastUserEnd=("我 · 中文原稿\n"+question).Length});
   hidden.Update("hidden",new ConversationDocument{Text="我 · 中文原稿\n"+question+"\nClaude · 中文翻译\n"+Answer(30),LastUserId="u:late",LastUserEnd=("我 · 中文原稿\n"+question).Length});
   hidden.Arrange();
   Console.WriteLine("METRIC: first revealed question bottom="+hidden.Bottom(question)+" readingBottom="+hidden.ReadingBottom+" viewport="+hidden.Box.ViewportHeight+" offset="+hidden.Box.VerticalOffset);
   Check(Math.Abs(hidden.Bottom(question)-hidden.ReadingBottom)<3,"a previously hidden reading window anchors after its first layout");
  }
  using(var longQuestion=new Surface(true)){
   string content=new string('问',180)+"\n请先解释第一步。";
   history.User("multiline",new ChatUserMessage{Id="wrapped",Order=1},content+"\n\n",true,false);
   var multiline=history.Document("multiline");
   Check(multiline.Text.Substring(0,multiline.LastUserEnd).EndsWith("请先解释第一步。"),"trailing empty lines are not mistaken for the user's final sentence");
   longQuestion.Update("multiline",multiline);
   Check(Math.Abs(longQuestion.Bottom("请先解释第一步。")-longQuestion.ReadingBottom)<3,"long wrapped questions keep their last sentence visible at the bottom");
  }
  using(var bounded=new Surface(true)){
   for(int i=0;i<30;i++){history.User("bounded",new ChatUserMessage{Id="q"+i,Order=2*i},"这是第 "+i+" 个问题。",true,false);history.Reply("bounded","a"+i,2*i+1,Answer(2));}
   bounded.Update("bounded",history.Document("bounded"));
   history.User("bounded",new ChatUserMessage{Id="q30",Order=60},question,true,false);bounded.Update("bounded",history.Document("bounded"));
   history.Reply("bounded","a30",61,Answer(40));bounded.Update("bounded",history.Document("bounded"));
   Check(Math.Abs(bounded.Bottom(question)-bounded.ReadingBottom)<3,"pruning older history cannot shift the current question when its answer arrives");
  }
  Console.WriteLine("RESULT: "+count+" PASSED");return 0;
 }
}
