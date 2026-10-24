using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yuqiao;

class SettingsVoiceV17Probe {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);count++;Console.WriteLine("PASS "+name);}
 static object Field(object o,string name){return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(o);}
 static void Set(object o,string name,object v){o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(o,v);}
 static void Call(object o,string name){o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);}
 static Border Find(Canvas canvas,string id){foreach(UIElement child in canvas.Children){Border b=child as Border;if(b!=null&&(string)b.Tag==id)return b;}return null;}
 static int Paths(DependencyObject root){int n=root is System.Windows.Shapes.Path?1:0;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)n+=Paths(VisualTreeHelper.GetChild(root,i));return n;}
 static NavigationDisplayRow Row(string id,string text,string kind,double y){return new NavigationDisplayRow{Id=id,Text=text,Kind=kind,Bounds=new Rect(400,y*1.5,800,24)};}
 [STAThread] static int Main(string[] args){
  string product=Path.GetFullPath(args[0]);string assembly=AssemblyName.GetAssemblyName(product).Name;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>new AssemblyName(e.Name).Name==assembly?Assembly.LoadFrom(product):null;
  using(var sha=SHA256.Create())using(var f=File.OpenRead(product))Console.WriteLine("PRODUCT_SHA256 "+BitConverter.ToString(sha.ComputeHash(f)).Replace("-",""));
  return Run();
 }
 static int Run(){try{
  var window=new FloatingNavigationWindow{Left=1110,Top=100,Height=740};
  var rows=new List<NavigationDisplayRow>{Row("appearance","外观","section",170),Row("font","界面字体：默认·系统","setting",220),Row("motion","动态效果：跟随系统","setting",280),Row("general","通用","settings-tab",240)};
  Rect dialog=new Rect(200,100,900,740),content=new Rect(410,140,690,700);
  window.UpdateSettingsAligned(rows,"本地词典",dialog,content,1.5,"通用");
  Canvas canvas=(Canvas)Field(window,"settingsCanvas");
  Check(canvas.Children.Count==3,"current settings exclude navigation rows");
  Border font=Find(canvas,"font");
  Check(font!=null&&Math.Abs(Canvas.GetTop(font)+window.Top+59-220)<.01,"Chinese label aligns to physical source Y at 150 percent scale");
  Check((string)font.ToolTip=="界面字体：默认·系统","label and value stay one complete item");
  Check(Paths(font)==0,"settings rows contain no invented icons");
  Check(font.MaxHeight<=57,"row height cannot overwrite next source item");
  window.UpdateSettingsAligned(rows,"status changed",dialog,content,1.5,"通用");
  Check(Object.ReferenceEquals(font,Find(canvas,"font")),"polling preserves unchanged row instances");
  ((Button)Field(window,"settingsTabsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  Check(canvas.Children.Count==1&&Find(canvas,"general")!=null,"left-column mode contains only navigation");
  Check(Math.Abs(Canvas.GetTop(Find(canvas,"general"))+159-240)<.01,"navigation preserves original vertical position");
  ((Button)Field(window,"settingsBodyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  rows[1].Bounds=new Rect(400,195*1.5,800,24);
  window.UpdateSettingsAligned(rows,"",dialog,content,1.5,"通用");
  Check(Math.Abs(Canvas.GetTop(Find(canvas,"font"))+159-195)<.01,"scrolling moves Chinese row with its original label");
  window.Follow.IsChecked=false;
  Check(((StackPanel)Field(window,"settingsList")).Children.Count==3,"manual placement offers readable compact rows");
  window.UpdateSettingsAligned(new List<NavigationDisplayRow>(),"",dialog,content,1.5,"隐私");
  var empty=(StackPanel)Field(window,"settingsList");
  Check(empty.Children.Count==1&&((TextBlock)empty.Children[0]).Text.Contains("暂未适配"),"unknown page shows explicit unsupported message");
  window.UpdateAligned(new List<NavigationDisplayRow>(),new List<NavigationDisplayRow>(),"",false,Rect.Empty,Rect.Empty,1.5);
  Check(window.Title=="语桥 · 中文菜单","leaving settings restores menu mode");
  Check(!window.IsVisible,"settings QA never displayed a window");
  window.ClosePermanently();
  var bridge=new BridgeWindow(new[]{"--test-window","123"});
  var lane=(Lane)Field(bridge,"outgoing");lane.Source.Text="保留已有中文";
  int calls=0;TextBox selected=null;Set(bridge,"systemVoiceLauncher",new Action<TextBox>(editor=>{calls++;selected=editor;}));
  Call(bridge,"ToggleVoice");
  Check(calls==1&&Object.ReferenceEquals(selected,lane.Source),"voice command targets local Chinese input using mocked launcher");
  Check(lane.Source.Text=="保留已有中文"&&!lane.Source.IsReadOnly,"voice launch preserves editable draft");
  Check((string)((Button)Field(bridge,"voiceButton")).Content=="结束听写","active voice entry can end system listening");
  Check((bool)Field(bridge,"systemVoiceRequested"),"successful request retains system-input state");
  Set(bridge,"systemVoiceRequested",false);Set(bridge,"systemVoiceLauncher",new Action<TextBox>(editor=>{throw new InvalidOperationException("模拟系统拒绝");}));Call(bridge,"ToggleVoice");
  Check((bool)Field(bridge,"voiceError")&&((string)Field(bridge,"voiceHint")).Contains("模拟系统拒绝"),"system-launch failure is visible without recording fallback");
  Set(bridge,"filling",true);Set(bridge,"systemVoiceLauncher",new Action<TextBox>(editor=>calls++));Call(bridge,"ToggleVoice");
  Check(calls==1,"voice request is blocked while filling target input");
  var input=new FloatingInputWindow();Check((string)input.Voice.Content=="语音输入","floating voice entry uses same system-input label");input.ClosePermanently();
  Check(typeof(WindowsVoiceInput).Assembly.GetType("Yuqiao.VoiceInputService")==null,"old internal recognizer has been removed from product");
  bool refused=false;try{WindowsVoiceInput.Request(null);}catch(InvalidOperationException){refused=true;}Check(refused,"missing editor is rejected before native input");
  Array sequence=(Array)typeof(WindowsVoiceInput).GetMethod("Shortcut",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
  int[] keys={0x5B,0x48,0x48,0x5B};uint[] flags={0u,0u,2u,2u};bool correct=sequence.Length==4;
  for(int i=0;i<sequence.Length;i++){object data=sequence.GetValue(i).GetType().GetField("data").GetValue(sequence.GetValue(i));object key=data.GetType().GetField("keyboard").GetValue(data);correct&=(ushort)key.GetType().GetField("key").GetValue(key)==keys[i]&&(uint)key.GetType().GetField("flags").GetValue(key)==flags[i];}
  Check(correct,"shortcut plan presses then releases Win and H without executing it");
  Check(!bridge.IsVisible,"voice tests never displayed UI or launched system listening");bridge.Close();
  Console.WriteLine("ALL "+count+" CHECKS PASSED; no real Win+H, audio, UIA or network");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
