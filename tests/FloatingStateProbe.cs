// Invisible state/layout QA only. Stops placement timer before pumping Dispatcher.
// Never calls ShowFullMode, Composer, reader.Read, or any real input APIs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

internal sealed class FloatingFakeHandler : HttpMessageHandler
{
    public int Requests;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        Interlocked.Increment(ref Requests);
        bool google=request.RequestUri.Host.Contains("googleapis");
        await Task.Delay(google?450:100).ConfigureAwait(false);
        string text=google?"Google の別の候補です。":"おすすめの日本語です。";
        string json=google?"{\"sentences\":[{\"trans\":\""+text+"\"}]}":"[{\"translations\":[{\"text\":\""+text+"\"}]}]";
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
    }
}
internal static class FloatingStateProbe
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly List<string> results=new List<string>();static int failed;
    static object Get(object obj,string field){return obj.GetType().GetField(field,Flags).GetValue(obj);}
    static void Set(object obj,string field,object value){obj.GetType().GetField(field,Flags).SetValue(obj,value);}
    static object Call(object obj,string method,params object[] args){try{return obj.GetType().GetMethod(method,Flags).Invoke(obj,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
    static TextBox Box(object obj,string field){return (TextBox)Get(obj,field);}
    static void Check(string name,bool ok,string detail){results.Add((ok?"PASS":"FAIL")+"\t"+name+"\t"+detail);if(!ok)failed++;}
    static void Pump(Task task)
    {
        Stopwatch watch=Stopwatch.StartNew();
        while(!task.IsCompleted&&watch.ElapsedMilliseconds<5000){DispatcherFrame frame=new DispatcherFrame();DispatcherTimer timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        if(!task.IsCompleted)throw new TimeoutException("Fixture task exceeded 5 seconds.");task.GetAwaiter().GetResult();
    }
    static void Composition(TextBox input,RoutedEvent phase){TextComposition text=new TextComposition(InputManager.Current,input,"pin");input.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,text){RoutedEvent=phase});}

    [STAThread] public static int Main(string[] args)
    {
        Window window=null;
        try{
            string assemblyPath=Path.GetFullPath(args[0]);
            using(SHA256 sha=SHA256.Create())using(FileStream source=File.OpenRead(assemblyPath))results.Add("Compiled-product SHA256\t"+BitConverter.ToString(sha.ComputeHash(source)).Replace("-","").ToLowerInvariant());
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Assembly product=Assembly.LoadFrom(assemblyPath);
            window=(Window)Activator.CreateInstance(product.GetType("Yuqiao.BridgeWindow"),new object[]{new[]{"--test-window","0x1234"}});
            // Empty source ensures entering this mode cannot launch translations.
            Call(window,"ShowFloatingMode");
            ((DispatcherTimer)Get(window,"placementTimer")).Stop();
            Window input=(Window)Get(window,"floatInput"),read=(Window)Get(window,"floatRead");
            object lane=Get(window,"outgoing");TextBox composer=Box(input,"Input");ListBox candidates=(ListBox)Get(input,"Candidates");
            Check("mode binding never shows a window",!window.IsVisible&&!input.IsVisible&&!read.IsVisible&&!((DispatcherTimer)Get(window,"placementTimer")).IsEnabled,"all placement/UIA work disabled before Dispatcher pump");
            Check("main window retains taskbar entry",window.ShowInTaskbar&&window.WindowState==WindowState.Minimized,"user can recover the main window through its taskbar entry");
            IDisposable old=(IDisposable)Get(window,"service");FloatingFakeHandler handler=new FloatingFakeHandler();
            object fake=Activator.CreateInstance(product.GetType("Yuqiao.TranslationService"),Flags,null,new object[]{handler},null);Set(window,"service",fake);old.Dispose();
            Set(window,"connected",true);Set(window,"boundConversationKey","claude|123|https://claude.ai/chat/fixture");
            composer.Text="请明天下午联系我。";
            Check("floating input synchronizes main source",Box(lane,"Source").Text==composer.Text&&(string)Get(lane,"Seen")==composer.Text,"actual ShowFloatingMode TextChanged binding");
            Set(lane,"Changed",DateTime.UtcNow.AddSeconds(-2));Pump((Task)Call(window,"Schedule",lane,true));
            Pump((Task)Get(lane,"Pending"));Call(window,"UpdateFillState");
            Check("first candidate arrives independently",candidates.Items.Count==1,"primary faster than delayed alternate");
            candidates.SelectedIndex=0;string selected=candidates.SelectedItem.ToString();
            Pump((Task)Get(window,"alternativePending"));Call(window,"SyncFloating");
            Check("alternate appended keeps selection",candidates.Items.Count==2&&candidates.SelectedItem.ToString()==selected,"existing chosen text preserved when another provider arrives");
            Composition(composer,TextCompositionManager.PreviewTextInputStartEvent);
            Check("IME immediately disables fill",!(bool)Call(window,"CanFillJapanese")&&!((Button)Get(input,"Fill")).IsEnabled,"mouse path as well as shortcuts is blocked");
            Composition(composer,TextCompositionManager.PreviewTextInputEvent);
            Check("IME completion restores valid candidate",(bool)Call(window,"CanFillJapanese")&&((Button)Get(input,"Fill")).IsEnabled,"unchanged committed source remains valid");
            composer.Text="修改后的中文。";
            Check("edit clears candidates immediately",candidates.Items.Count==0&&!((Button)Get(input,"Fill")).IsEnabled,"stale candidate disappears synchronously");
            Set(lane,"Changed",DateTime.UtcNow.AddSeconds(-2));int requests=handler.Requests;
            Composition(composer,TextCompositionManager.PreviewTextInputStartEvent);Pump((Task)Call(window,"Schedule",lane,true));
            Check("IME does not start network requests",handler.Requests==requests&&(string)Get(lane,"Done")!="修改后的中文。","Schedule exits before provider requests");
            Composition(composer,TextCompositionManager.PreviewTextInputEvent);
            Task late=(Task)Call(window,"RunAlternative","旧的替代候选。",(int)Get(lane,"Version"),(int)Get(window,"epoch"),CancellationToken.None);
            composer.Text="最新的中文。";Pump(late);
            Check("late alternate cannot enter new source",(string)Get(window,"alternativeSource")!="旧的替代候选。"&&candidates.Items.Count==0,"uncancelled old response rejected by source version");

            FrameworkElement shell=(FrameworkElement)input.Content;
            shell.Measure(new Size(input.Width,input.Height));shell.Arrange(new Rect(0,0,input.Width,input.Height));shell.UpdateLayout();
            Button fill=(Button)Get(input,"Fill");Point position=fill.TranslatePoint(new Point(0,0),shell);
            ScrollViewer scroller=FindScrollViewer(shell);
            results.Add("LAYOUT\tFillTop="+position.Y.ToString("0.0")+"; FillBottom="+(position.Y+fill.ActualHeight).ToString("0.0")+"; WindowHeight="+input.Height.ToString("0.0")+"; ScrollableHeight="+(scroller==null?-1:scroller.ScrollableHeight).ToString("0.0"));
            Check("fill visible without scrolling",position.Y>=0&&position.Y+fill.ActualHeight<=input.Height-5,"invisible Measure/Arrange at default size");
            var auto=(CheckBox)Get(input,"AutoFill");var status=(TextBlock)Get(input,"Status");
            Check("auto switch and fill status visible without scrolling",auto.TranslatePoint(new Point(0,0),shell).Y+auto.ActualHeight<=input.Height-5&&status.TranslatePoint(new Point(0,0),shell).Y+status.ActualHeight<=input.Height-5,"new automatic controls fit in compact window");
            string draft=composer.Text;Call(window,"Pause","fixture pause");
            Check("pause retains shared Chinese draft",composer.Text==draft&&Box(lane,"Source").Text==draft&&candidates.Items.Count==0,"both local views retain the same draft");
            Check("isolated mode stayed invisible",!window.IsVisible&&!input.IsVisible&&!read.IsVisible&&!((DispatcherTimer)Get(window,"placementTimer")).IsEnabled,"no Show/Activate/reader/Composer path used");
        }catch(Exception ex){failed++;results.Add("FAIL\tUnexpected exception\t"+ex);}
        finally{if(window!=null)window.Close();results.Add("RESULT\t"+(failed==0?"ALL PASSED":"FAILED")+"\tFailures="+failed);File.WriteAllLines(args[1],results,Encoding.UTF8);foreach(string line in results)Console.WriteLine(line);}
        return failed==0?0:1;
    }
    static ScrollViewer FindScrollViewer(System.Windows.DependencyObject value){if(value is ScrollViewer)return (ScrollViewer)value;int count=System.Windows.Media.VisualTreeHelper.GetChildrenCount(value);for(int i=0;i<count;i++){ScrollViewer found=FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(value,i));if(found!=null)return found;}return null;}
}
