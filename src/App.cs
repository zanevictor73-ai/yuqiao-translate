using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation;
using System.IO;
using System.Collections.Generic;

namespace Yuqiao {
 public static class Entry {
  [STAThread] public static void Main(string[] args) {
   using(var instance=new Mutex(false,AppRuntime.MutexName)){
    bool acquired;try{acquired=instance.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
    if(!acquired){try{using(var wake=EventWaitHandle.OpenExisting(AppRuntime.MutexName+"_Show"))wake.Set();}catch(WaitHandleCannotBeOpenedException){}return;}
    try{
     var app=new Application();
     app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show("窗口遇到问题，请关闭后重新打开。\n"+e.Exception.GetType().Name,"语桥");e.Handled=true;};
     try{
      var window=new BridgeWindow(args);
      using(var wake=new EventWaitHandle(false,EventResetMode.AutoReset,AppRuntime.MutexName+"_Show")){
       var registration=ThreadPool.RegisterWaitForSingleObject(wake,(s,t)=>{if(!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke(new Action(()=>window.RestoreForUser()));},null,Timeout.Infinite,false);
       try{app.Run(window);}finally{registration.Unregister(null);}
      }
     }
     catch(ArgumentException ex){MessageBox.Show(ex.Message,"语桥 · 启动参数");app.Shutdown(2);}
    }finally{instance.ReleaseMutex();}
   }
  }
 }
 public sealed class Lane {
  public TextBox Source, First, Second; public TextBlock Status;
  public string Seen="", Done="", Id=""; public DateTime Changed; public int Version; public CancellationTokenSource Cancel; public Task Pending;
 }
 public sealed partial class BridgeWindow:Window {
  readonly TranslationService service=new TranslationService(); ChatWindowReader reader; readonly IntPtr testWindow;
  readonly DispatcherTimer timer=new DispatcherTimer(); readonly Lane outgoing=new Lane(), incoming=new Lane();
  TextBlock connection,footer,fillStatus; CheckBox active,pin,forceJapanese; ComboBox target; Button reconnect,fillTranslation; string profile="claude", boundTitle="", boundConversationKey="", lastReplyId="", lastReply="";
  bool busy,closed,baseline,suppressActiveEvents,newConversationTransitionAvailable,updatingSource,connected,filling; int epoch; string diagnosticFile;
  FloatingInputWindow floatInput; FloatingReadWindow floatRead; bool floatingMode,syncFloating,placing,mainComposing,inspectFloating;
  readonly DispatcherTimer placementTimer=new DispatcherTimer();
  readonly AutomaticDraft automaticDraft=new AutomaticDraft();
  CheckBox autoFill; bool automaticFilling;
  CancellationTokenSource autoFillCancellation;
  DateTime lastVerifiedFill=DateTime.MinValue;
  Action<string,string,string,CancellationToken> automaticComposer=(key,text,previous,token)=>ClaudeComposer.FillJapanese("claude",key,text,previous,token);
  Button voiceButton; TextBlock voiceStatus;
  bool voiceError, systemVoiceRequested;
  string voiceHint="语音输入使用 Windows＋H；请先选择中文输入法，在系统面板结束听写。";
  Action<TextBox> systemVoiceLauncher=WindowsVoiceInput.Request;
  DateTime voiceRequestedAt;
  readonly DispatcherTimer navigationTimer=new DispatcherTimer();
  NavigationTranslator navigationTranslator; NavigationContent navigationView; FloatingNavigationWindow floatNavigation;
  CheckBox navigationEnabled; bool navigationBusy; int navigationEpoch;
  string alternativeText="",alternativeProvider="",alternativeSource="",alternativeStatus="";
  Task alternativePending;
  string candidateSignature="";
  sealed class Choice { public string Text,Provider; public override string ToString(){return Provider+"\n"+Text;} }
  readonly Brush blue=new SolidColorBrush(Color.FromRgb(39,88,173));
  public BridgeWindow(string[] args) {
   Title="语桥实时翻译 · 桌面伴侣"; Width=510; Height=Math.Min(850,SystemParameters.WorkArea.Height-40); MinWidth=480;MinHeight=Math.Min(620,Height);
   FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;Background=new SolidColorBrush(Color.FromRgb(244,247,251));
   WindowStartupLocation=WindowStartupLocation.Manual;Left=10;Top=20;
   for(int i=0;i<args.Length;i++){
    if(args[i]=="--diagnostics"){
     if(++i>=args.Length)throw new ArgumentException("--diagnostics 需要指定文件路径。");
     diagnosticFile=Path.GetFullPath(args[i]);
    }else if(args[i]=="--test-window"){
     if(++i>=args.Length)throw new ArgumentException("--test-window 需要指定窗口句柄。");
     long handle;string value=args[i];
     if(value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)){
      if(!Int64.TryParse(value.Substring(2),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out handle))throw new ArgumentException("测试窗口句柄格式不正确。");
     }else if(!Int64.TryParse(value,out handle))throw new ArgumentException("测试窗口句柄格式不正确。");
     if(handle==0)throw new ArgumentException("测试窗口句柄不能为零。");
     testWindow=new IntPtr(handle);
    }else if(args[i]=="--inspect-floating"){inspectFloating=true;}
    else if(args[i]=="--target"){if(++i>=args.Length||AppCatalog.Find(args[i])==null)throw new ArgumentException("请选择支持的目标软件。");profile=AppCatalog.Get(args[i]).Id;}
    else throw new ArgumentException("无法识别的启动参数："+args[i]);
   }
   if(testWindow==IntPtr.Zero){profile=AppPreferences.Load().SelectedAppId;for(int i=0;i<args.Length-1;i++)if(args[i]=="--target")profile=AppCatalog.Get(args[i+1]).Id;}
   reader=CreateReader();newConversationTransitionAvailable=profile=="claude";
   var outer=new Grid{Margin=new Thickness(22)}; Content=outer;
   outer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});outer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});outer.RowDefinitions.Add(new RowDefinition());outer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var header=new StackPanel();outer.Children.Add(header);
   header.Children.Add(new TextBlock{Text="语桥  ·  实时翻译",FontSize=26,FontWeight=FontWeights.SemiBold,Foreground=blue});
   header.Children.Add(new TextBlock{Text="说中文或写中文，选择日文或英文填入 Claude；由你确认发送",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16),Foreground=Brushes.DimGray});
   var bar=new StackPanel();Grid.SetRow(bar,1);outer.Children.Add(bar);
   var targetRow=new DockPanel{LastChildFill=false,Margin=new Thickness(0,0,0,10)};bar.Children.Add(targetRow);
   targetRow.Children.Add(new TextBlock{Text="监听目标",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});
   target=new ComboBox{MinWidth=230,Padding=new Thickness(7,4,7,4)};foreach(var candidate in AppCatalog.All)target.Items.Add(candidate);for(int i=0;i<target.Items.Count;i++)if(((SupportedApp)target.Items[i]).Id==profile)target.SelectedIndex=i;AutomationProperties.SetName(target,"监听目标");targetRow.Children.Add(target);
   connection=new TextBlock{Text="正在连接 "+TargetName()+"…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};bar.Children.Add(connection);
   var controls=new WrapPanel();bar.Children.Add(controls);
   active=new CheckBox{Content="自动翻译",IsChecked=true,Margin=new Thickness(0,7,18,7),VerticalContentAlignment=VerticalAlignment.Center};controls.Children.Add(active);
   autoFill=new CheckBox{Content="自动填入日文",IsChecked=true,Margin=new Thickness(0,7,18,7),ToolTip="目前用于 Claude；仅更新助手自己写入的日文，保留其他草稿。"};controls.Children.Add(autoFill);
   autoFill.Checked+=(s,e)=>{automaticDraft.InputChanged();UpdateFillState();};autoFill.Unchecked+=(s,e)=>{if(autoFillCancellation!=null)autoFillCancellation.Cancel();fillStatus.Text="已关闭自动填入；仍可手动填入所选译文，不会发送。";UpdateFillState();};
   pin=new CheckBox{Content="窗口置顶",Margin=new Thickness(0,7,18,7)};controls.Children.Add(pin);pin.Checked+=(s,e)=>Topmost=true;pin.Unchecked+=(s,e)=>Topmost=false;
   reconnect=Button("重新连接",()=>Reset());controls.Children.Add(reconnect);
   controls.Children.Add(Button("使用说明",()=>ShowHelp()));
   controls.Children.Add(Button("悬浮模式",()=>ShowFloatingMode()));
   navigationEnabled=new CheckBox{Content="目录与菜单",IsChecked=true,Margin=new Thickness(0,7,8,7),ToolTip="目录和头像菜单只显示中文及图标；可见会话标题会联网翻译。"};controls.Children.Add(navigationEnabled);
   navigationEnabled.Checked+=(s,e)=>ResetNavigation();navigationEnabled.Unchecked+=(s,e)=>ResetNavigation();
   active.Checked+=(s,e)=>{if(!suppressActiveEvents)Reset();};active.Unchecked+=(s,e)=>{if(!suppressActiveEvents)Pause("已暂停 · 重新勾选后继续");};
   target.SelectionChanged+=(s,e)=>{SupportedApp selected=target.SelectedItem as SupportedApp;if(selected==null)return;profile=selected.Id;if(IsLoaded&&testWindow==IntPtr.Zero){try{var preferences=AppPreferences.Load();preferences.SelectedAppId=profile;preferences.Save();}catch(Exception){MessageBox.Show(this,"当前选择已生效，但未能保存，下次可能恢复默认。","语桥");}}Reset();};
   var tabs=new TabControl{FontSize=12,Margin=new Thickness(0,16,0,12),Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(220,227,237))};Grid.SetRow(tabs,2);outer.Children.Add(tabs);
   tabs.Items.Add(new TabItem{Header="  中文输入 → 日文 / 英文  ",Content=MakeLane(outgoing,true)});
   tabs.Items.Add(new TabItem{Header="  当前页面 → 中文  ",Content=MakeLane(incoming,false)});
   navigationView=new NavigationContent{Margin=new Thickness(12)};
   tabs.Items.Add(new TabItem{Header="  目录 / 头像菜单  ",Content=new ScrollViewer{Content=navigationView,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}});
   navigationTranslator=new NavigationTranslator(service);navigationTranslator.Updated+=ApplyNavigation;
   footer=new TextBlock{Text="日文 / 英文自动填入 · 回车发送后清空中文 · Shift + Enter 换行\n普通联网翻译交给 Microsoft / Google；不需要大模型密钥。",FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray};Grid.SetRow(footer,3);outer.Children.Add(footer);
   System.Windows.Input.TextCompositionManager.AddPreviewTextInputStartHandler(outgoing.Source,(s,e)=>{mainComposing=true;UpdateFillState();});
   System.Windows.Input.TextCompositionManager.AddPreviewTextInputUpdateHandler(outgoing.Source,(s,e)=>{mainComposing=true;UpdateFillState();});
   outgoing.Source.PreviewTextInput+=(s,e)=>{mainComposing=false;UpdateFillState();};
   outgoing.Source.LostKeyboardFocus+=(s,e)=>{mainComposing=false;UpdateFillState();};
   timer.Interval=TimeSpan.FromMilliseconds(300);timer.Tick+=async(s,e)=>await Tick();
   navigationTimer.Interval=TimeSpan.FromMilliseconds(850);navigationTimer.Tick+=async(s,e)=>await ReadNavigation();
   Loaded+=(s,e)=>{timer.Start();navigationTimer.Start();};
   placementTimer.Interval=TimeSpan.FromMilliseconds(700);placementTimer.Tick+=async(s,e)=>await PlaceFloating();

   Deactivated+=(s,e)=>Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{if(!closed&&!floatingMode&&!IsActive)StopVoice();}));
   StateChanged+=(s,e)=>{if(floatingMode&&WindowState!=WindowState.Minimized)ShowFullMode();else if(!floatingMode&&WindowState==WindowState.Minimized)StopVoice();};
   Closed+=(s,e)=>{closed=true;CancelVoice();timer.Stop();placementTimer.Stop();navigationTimer.Stop();navigationTranslator.Dispose();Cancel(outgoing);Cancel(incoming);if(floatInput!=null)floatInput.ClosePermanently();if(floatRead!=null)floatRead.ClosePermanently();if(floatNavigation!=null)floatNavigation.ClosePermanently();if(panelControls!=null)panelControls.Close();service.Dispose();};InitializeConversationUi(controls,tabs);InitializeLanguageUi(controls);UpdateFillState();
  }
  Button Button(string title,Action action){var b=new Button{Content=title,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0),Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(211,220,233))};b.Click+=(s,e)=>action();return b;}
  TextBox TextArea(string name,int height){var t=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.White,BorderThickness=new Thickness(0),Padding=new Thickness(9),FontSize=16,MinHeight=height};AutomationProperties.SetName(t,name);return t;}
  UIElement MakeLane(Lane lane,bool send){
   var panel=new StackPanel{Margin=new Thickness(16)};
   panel.Children.Add(new TextBlock{Text=send?"中文输入（保留在本工具）":"当前最新回复（自动读取）",FontWeight=FontWeights.SemiBold,Foreground=blue,Margin=new Thickness(0,0,0,6)});
   lane.Source=TextArea(send?"中文输入":"当前回复原文",70);lane.Source.Height=send?95:105;lane.Source.IsReadOnly=!send;
   lane.Source.Background=send?Brushes.White:new SolidColorBrush(Color.FromRgb(246,248,252));
   if(send){lane.Source.BorderThickness=new Thickness(1);lane.Source.BorderBrush=new SolidColorBrush(Color.FromRgb(190,205,226));lane.Source.TextChanged+=(s,e)=>{if(!updatingSource){voiceError=false;automaticDraft.InputChanged();Observe(outgoing,lane.Source.Text,false);UpdateFillState();}};}
   panel.Children.Add(lane.Source);
   if(send){
    var voiceRow=new DockPanel{Margin=new Thickness(0,8,0,0),LastChildFill=true};
    voiceButton=Button("语音输入",()=>ToggleVoice());voiceButton.VerticalAlignment=VerticalAlignment.Top;AutomationProperties.SetName(voiceButton,"语音输入");voiceRow.Children.Add(voiceButton);
    voiceStatus=new TextBlock{Text=voiceHint,FontSize=11,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};voiceRow.Children.Add(voiceStatus);panel.Children.Add(voiceRow);
   }
   lane.First=TextArea(send?"英文译文":"中文译文",70);lane.First.Height=send?95:150;
   if(send){
    lane.Second=TextArea("日文译文",70);lane.Second.Height=105;
    AddResult(panel,"日本語  日文译文（可选择自动填入）",lane.Second);
    fillTranslation=Button("填入日文",()=>{FillCurrentTranslation();});fillTranslation.HorizontalAlignment=HorizontalAlignment.Left;fillTranslation.Margin=new Thickness(0,9,0,5);panel.Children.Add(fillTranslation);
    fillStatus=new TextBlock{Text="所选语言完成后自动填入；按回车发送，确认后清空本次中文。",FontSize=12,Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap};panel.Children.Add(fillStatus);
    AddResult(panel,"ENGLISH  英文译文（可选择自动填入）",lane.First);
   }else AddResult(panel,"简体中文 · 阅读辅助",lane.First);
   lane.Status=new TextBlock{Text=send?"在上方输入中文，停顿后自动翻译…":"连接后自动翻译当前最新日文或英文回复…",Margin=new Thickness(0,12,0,0),FontSize=12,Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap};panel.Children.Add(lane.Status);
   if(!send){
    forceJapanese=new CheckBox{Content="把当前回复按日文翻译（用于纯汉字日文）",FontSize=12,Margin=new Thickness(0,10,0,0)};panel.Children.Add(forceJapanese);forceJapanese.Checked+=(s,e)=>InvalidateIncoming();forceJapanese.Unchecked+=(s,e)=>InvalidateIncoming();
    panel.Children.Add(new TextBlock{Text="左侧目录、最近会话和头像菜单的中文对照，见“目录 / 头像菜单”页。悬浮模式下会显示独立中文小窗。",Margin=new Thickness(0,18,0,6),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray});
   }
   return new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=panel};
  }
  void AddResult(StackPanel panel,string title,TextBox result){var row=new DockPanel{LastChildFill=false,Margin=new Thickness(0,12,0,4)};row.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});var copy=Button("复制",()=>Copy(result.Text));DockPanel.SetDock(copy,Dock.Right);row.Children.Add(copy);panel.Children.Add(row);panel.Children.Add(result);}
  void Copy(string text){if(String.IsNullOrEmpty(text))return;try{Clipboard.SetText(text);}catch{MessageBox.Show(this,"剪贴板正忙，请再试一次。","语桥");}}
  void ShowHelp(){MessageBox.Show(this,"1. 在监听目标中选择 Claude、ChatGPT 或 DeepSeek Harness，再打开对应客户端。默认是 Claude。\n2. 在本工具的“中文输入”框里写中文，停顿约 0.6 秒后自动显示日文和英文。也可点“语音输入”，光标会回到中文框并调用 Windows＋H。请先选择中文输入法，在 Windows 面板结束听写；系统可能联网，请核对识别文字。\n3. 在“发送语言”中选日文或英文。Claude 默认开启自动填入：所选译文完成后写入 Claude；普通输入会把光标交回 Claude。按回车发送，确认消息出现后自动清空本次中文。中文框按 Shift + Enter 换行；拼音选字时回车不会发送。语音听写期间光标留在中文框，回车会结束听写并发送。继续修改中文会更新本助手写入的译文；手动修改或清空 Claude 草稿后会停下，等待新的中文。可取消“自动填入”，改用手动填入。ChatGPT 为实验适配，当前版本未开放填入。\n4. 切换悬浮模式后，可用 Alt + ↑ / ↓ 选择不同日文措辞。旁边的对话窗保留你的中文原稿和 Claude 的中文译文；已有消息会标注“原文译回中文”。顶端小条可以分别显示或隐藏三个小窗。“目录与菜单”小窗会同步 Claude 左侧目录、最近会话标题和头像菜单的纯中文与图标，并贴近原侧栏逐行显示。也可在完整窗口的“目录 / 头像菜单”页查看。原界面保持日文，操作仍点击原菜单。\n5. 切换到另一段聊天后会暂停，请点“重新连接”。暂停和重连会保留本地中文草稿。\n6. 手动打开语桥即可使用，关闭窗口即退出。右侧中文对话以你的最新提问为阅读起点，译文更新保留你的阅读位置。\n\n实际兼容与客户端版本有关，已验收范围见随包说明。自动识别含假名的日文和英文回复；纯汉字日文可勾选按日文翻译。回复会在文字短暂停顿后更新，单条最多 5000 字。\n\n翻译通过 Microsoft / Google 网络服务完成，无需大模型密钥，但仍然调用翻译 API，并非离线或完全不使用 AI。免费通道可能限流或变化；短句以 3—4 秒为目标，网络异常可能更慢。聊天文字不写入设置文件，关闭后不恢复。","语桥 · 使用说明");}
  void Cancel(Lane lane){lane.Version++;if(Object.ReferenceEquals(lane,outgoing)){alternativeText="";alternativeProvider="";alternativeSource="";alternativeStatus="";}if(lane.Cancel!=null){lane.Cancel.Cancel();lane.Cancel.Dispose();lane.Cancel=null;}}
  ChatWindowReader CreateReader(){return testWindow==IntPtr.Zero?new ChatWindowReader(profile):new ChatWindowReader(testWindow);}
  void Pause(string status){ResetSendState();automaticDraft.Suspend();CancelVoice();epoch++;connected=false;Clear(outgoing);Clear(incoming);baseline=false;connection.Text=status;outgoing.Status.Text="已暂停，中文草稿已保留。";incoming.Status.Text="翻译已暂停，当前无有效译文。";if(!filling)fillStatus.Text="已暂停；中文草稿保留，修改中文后继续自动填入。";UpdateFillState();}
  void Clear(Lane lane){Cancel(lane);bool keepDraft=Object.ReferenceEquals(lane,outgoing);lane.Seen=keepDraft?lane.Source.Text:"";lane.Done="";lane.Id="";lane.Changed=DateTime.UtcNow;if(!keepDraft)lane.Source.Text="";lane.First.Text="";if(lane.Second!=null)lane.Second.Text="";}
  void InvalidateIncoming(){Cancel(incoming);incoming.Done="";incoming.Changed=DateTime.UtcNow;incoming.First.Text="";incoming.Status.Text=String.IsNullOrWhiteSpace(incoming.Seen)?"等待新的日文或英文回复…":"翻译方式已更新，等待片刻…";}
  void RestoreWaitingStatus(Lane lane,bool receive){if(String.IsNullOrWhiteSpace(lane.Seen)&&String.IsNullOrEmpty(lane.First.Text)&&(lane.Second==null||String.IsNullOrEmpty(lane.Second.Text)))lane.Status.Text=receive?WaitingReply:"在上方输入中文，停顿后自动翻译…";}
  void MarkDisconnected(string status){connected=false;connection.Text=status;if(baseline||!String.IsNullOrEmpty(incoming.Seen)){Clear(incoming);incoming.Status.Text="连接后自动读取当前最新日文或英文回复…";}baseline=false;UpdateFillState();}
  void Reset(){ResetSendState();automaticDraft.Suspend();ResetNavigation();CancelVoice();epoch++;connected=false;baseline=false;boundTitle="";boundConversationKey="";newConversationTransitionAvailable=profile=="claude";lastReplyId="";lastReply="";Clear(outgoing);Clear(incoming);outgoing.Status.Text=String.IsNullOrWhiteSpace(outgoing.Seen)?"在上方输入中文，停顿后自动翻译…":"中文草稿已保留，准备重新翻译…";incoming.Status.Text="连接后自动读取当前最新日文或英文回复…";if(!filling)fillStatus.Text="中文草稿已保留；修改中文后继续自动填入，不发送。";connection.Text="正在连接 "+TargetName()+"…";reader=CreateReader();suppressActiveEvents=true;try{active.IsChecked=true;}finally{suppressActiveEvents=false;}UpdateFillState();}
  string TargetName(){return AppCatalog.Get(profile).DisplayName.Replace(" 客户端","");}
  bool CanFillTranslation(){return !closed&&!filling&&!mainComposing&&(floatInput==null||!floatInput.IsComposing)&&active.IsChecked==true&&connected&&AppCatalog.Get(profile).CanFill&&boundConversationKey.StartsWith(profile+"|",StringComparison.Ordinal)&&!String.IsNullOrWhiteSpace(outgoing.Seen)&&outgoing.Done==outgoing.Seen&&!String.IsNullOrWhiteSpace(CurrentTranslation);}
  void UpdateFillState(){if(fillTranslation==null)return;fillTranslation.IsEnabled=CanFillTranslation();outgoing.Source.IsReadOnly=filling&&!automaticFilling;fillTranslation.Content="手动填入 "+TargetName()+"（"+LanguageName+"）";fillTranslation.ToolTip=connected?"自动填入已默认开启；此按钮用于手动重试，不会发送。":"请先连接 "+TargetName()+"。";if(autoFill!=null)autoFill.IsEnabled=profile=="claude";UpdateVoiceButton(voiceButton);if(voiceStatus!=null)voiceStatus.Text=VoiceStatusText();SyncLanguageUi();SyncFloating();}
  string VoiceStatusText(){return voiceHint;}
  void UpdateVoiceButton(Button button){
   if(button==null)return;
   button.Content=systemVoiceRequested?"结束听写":"语音输入";AutomationProperties.SetName(button,"语音输入");
   button.IsEnabled=!closed&&!filling&&!mainComposing&&(floatInput==null||!floatInput.IsComposing);
   button.Foreground=button==voiceButton?blue:FloatingUi.Accent;button.Background=button==voiceButton?Brushes.White:FloatingUi.Header;
   button.ToolTip="光标回到中文输入框并打开 Windows＋H。识别与结束由系统语音面板负责。";
  }
  async void ToggleVoice(){
   if(closed||filling||mainComposing||(floatInput!=null&&floatInput.IsComposing))return;
   TextBox editor=floatingMode&&floatInput!=null&&floatInput.IsVisible?floatInput.Input:outgoing.Source;
   try{
    if(systemVoiceRequested){systemVoiceEnder(editor);systemVoiceRequested=false;voiceHint="已结束听写；所选译文就绪后，光标回到 Claude。";UpdateFillState();if(testWindow==IntPtr.Zero&&CanFillTranslation())await Task.Run(()=>targetFocuser(boundConversationKey));return;}
    systemVoiceLauncher(editor);systemVoiceRequested=true;voiceRequestedAt=DateTime.UtcNow;voiceError=false;
    voiceHint="已打开 Windows 听写：请说中文。所选译文自动填入；回车结束听写并发送。";
   }catch(Exception ex){systemVoiceRequested=false;voiceError=true;voiceHint="未能打开系统语音输入："+ex.Message;}
   UpdateFillState();
  }
  // Windows owns the microphone and dictation lifetime. Never simulate a second
  // Win+H on focus changes: that could restart dictation in a different app.
  void StopVoice(){if(!WindowsVoiceInput.IsSystemInputForeground())systemVoiceRequested=false;}
  void CancelVoice(){systemVoiceRequested=false;}
  async Task TryAutomaticFill(ChatSnapshot snapshot){
   if(awaitingSendReceipt||sendBusy)return;
   if(profile!="claude"||autoFill.IsChecked!=true||!CanFillTranslation())return;
   if(testWindow==IntPtr.Zero&&(!ClaudeSend.IsInteractionForeground(boundConversationKey)||(requestedSendVersion>=0&&ClaudeSend.IsEnterHeld())))return;
   string desired=CurrentTranslation;
   if(floatingMode&&floatInput!=null){Choice choice=floatInput.Candidates.SelectedItem as Choice;if(choice!=null)desired=choice.Text;}
   if(!IsCurrentCandidate(desired))return;
   string previous;
   if(!automaticDraft.TryPlan(boundConversationKey,snapshot.Draft,desired,out previous)){
    if(automaticDraft.Status.Length>0&&requestedSendVersion<0)fillStatus.Text=automaticDraft.Status;return;
   }
   string key=boundConversationKey,chinese=outgoing.Source.Text;int generation=epoch,version=outgoing.Version;
   TextBox localEditor=floatingMode&&floatInput!=null?floatInput.Input:outgoing.Source;
   using(var focus=new LocalInputFocusGuard(systemVoiceRequested?localEditor:null,key))
   using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(outgoing.Cancel==null?CancellationToken.None:outgoing.Cancel.Token)){
    autoFillCancellation=cancel;automaticFilling=true;filling=true;fillStatus.Text="正在自动填入译文，不发送…";UpdateFillState();
    try{
     await Task.Run(()=>automaticComposer(key,desired,previous,cancel.Token));
     if(!closed&&generation==epoch&&key==boundConversationKey){automaticDraft.RecordVerified(key,desired);conversationFlow.Written(snapshot,key,chinese,desired,version);lastVerifiedFill=DateTime.UtcNow;fillStatus.Text="译文已填入；回车发送，发送后自动清空本次中文。";if(!systemVoiceRequested&&testWindow==IntPtr.Zero)await Task.Run(()=>targetFocuser(key));}
    }catch(OperationCanceledException){if(!closed)fillStatus.Text="中文或自动填入状态已变化，等待最新译文。";}
     catch(Exception ex){if(!closed){fillStatus.Text=ex is InvalidOperationException?ex.Message:"自动填入暂未成功，请检查 Claude 当前输入框。";automaticDraft.RecordFailure(fillStatus.Text);}}
    finally{autoFillCancellation=null;automaticFilling=false;filling=false;if(!closed)UpdateFillState();}
   }
  }
  async void FillCurrentTranslation(string selected=null){
   if(!CanFillTranslation()){fillStatus.Text="请先连接 "+TargetName()+"，并等待当前中文的所选译文完成。";return;}
   string expectedProfile=profile,expectedKey=boundConversationKey,japanese=selected??CurrentTranslation,chinese=outgoing.Source.Text;int version=outgoing.Version,generation=epoch;
   if(!IsCurrentCandidate(japanese)){fillStatus.Text="候选已过期，请等待当前中文重新翻译。";return;}
   filling=true;fillStatus.Text="正在把译文填入 "+TargetName()+"…";UpdateFillState();
   string previous=automaticDraft.Key==expectedKey?automaticDraft.OwnedText:null;
   try{await Task.Run(()=>{if(expectedProfile=="claude")ClaudeComposer.FillJapanese(expectedProfile,expectedKey,japanese,previous,CancellationToken.None);else GenericDesktopAdapter.FillJapanese(expectedProfile,expectedKey,japanese);});if(!closed){lastVerifiedFill=DateTime.UtcNow;if(expectedProfile=="claude"&&generation==epoch){automaticDraft.Observe(expectedKey,japanese);automaticDraft.RecordVerified(expectedKey,japanese);conversationFlow.Written(latestSnapshot??new ChatSnapshot(),expectedKey,chinese,japanese,version);}fillStatus.Text=version==outgoing.Version&&generation==epoch?"已填入译文，尚未发送。请到 "+TargetName()+" 确认后发送。":"已处理点击时的译文，请到目标软件核对；当前中文已变化。";}}
   catch(Exception ex){if(!closed)fillStatus.Text=ex is InvalidOperationException?ex.Message:"暂时无法填入，请确认目标窗口和当前会话。";}
   finally{filling=false;if(!closed)UpdateFillState();}
  }
  void ShowFloatingMode(){
   if(floatInput==null){
    floatInput=new FloatingInputWindow();floatRead=new FloatingReadWindow();
    InitializePanelControls();
    floatInput.HideRequested+=()=>{inputShown.IsChecked=false;};floatRead.HideRequested+=()=>{readingShown.IsChecked=false;};
    floatInput.SendRequested+=RequestSend;
    floatInput.Input.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape){systemVoiceRequested=false;UpdateFillState();}};
    // Expose independent tool windows to desktop QA without changing ordinary use.
    if(inspectFloating){floatInput.ShowInTaskbar=true;floatRead.ShowInTaskbar=true;}
    floatInput.Input.TextChanged+=(s,e)=>{if(!syncFloating&&(!filling||automaticFilling))outgoing.Source.Text=floatInput.Input.Text;};
    System.Windows.Input.TextCompositionManager.AddPreviewTextInputStartHandler(floatInput.Input,(s,e)=>UpdateFillState());
    System.Windows.Input.TextCompositionManager.AddPreviewTextInputUpdateHandler(floatInput.Input,(s,e)=>UpdateFillState());
    floatInput.Input.PreviewTextInput+=(s,e)=>UpdateFillState();floatInput.Input.LostKeyboardFocus+=(s,e)=>UpdateFillState();
    floatInput.FullMode.Click+=(s,e)=>ShowFullMode();floatRead.FullMode.Click+=(s,e)=>ShowFullMode();
    floatInput.Voice.Click+=(s,e)=>ToggleVoice();
    floatInput.LanguageSelector.SelectionChanged+=(s,e)=>{if(!syncingLanguage)SetConversationLanguage(floatInput.LanguageSelector.SelectedIndex==1?"en":"ja");};
    floatInput.Fill.Click+=(s,e)=>{Choice choice=floatInput.Candidates.SelectedItem as Choice;if(choice!=null)FillCurrentTranslation(choice.Text);};
    floatInput.Candidates.SelectionChanged+=(s,e)=>{if(!syncFloating){automaticDraft.InputChanged();if(autoFillCancellation!=null)autoFillCancellation.Cancel();UpdateFillState();}};
    floatInput.AutoFill.Checked+=(s,e)=>{if(!syncFloating)autoFill.IsChecked=true;};floatInput.AutoFill.Unchecked+=(s,e)=>{if(!syncFloating)autoFill.IsChecked=false;};
   }
   floatingMode=true;SyncFloating();placementTimer.Start();WindowState=WindowState.Minimized;
   ApplyNavigation();
   if(conversationLanguage=="ja"&&!String.IsNullOrWhiteSpace(outgoing.Second.Text)&&outgoing.Done==outgoing.Seen&&outgoing.Cancel!=null&&alternativeSource!=outgoing.Seen){alternativeStatus="第二种日文措辞正在翻译…";alternativePending=RunAlternative(outgoing.Seen,outgoing.Version,epoch,outgoing.Cancel.Token);}
  }
  void ShowFullMode(){
   floatingMode=false;placementTimer.Stop();if(floatInput!=null){floatInput.Hide();floatRead.Hide();}if(floatNavigation!=null)floatNavigation.Hide();if(panelControls!=null)panelControls.Hide();
   if(!closed){Show();WindowState=WindowState.Normal;Activate();}
  }
  public void RestoreForUser(){ShowFullMode();}
  void ResetNavigation(){
   navigationEpoch++;
   if(navigationTranslator!=null)navigationTranslator.Reset(profile!="claude"?"目录与菜单目前支持 Claude 客户端。":navigationEnabled.IsChecked==true?"正在读取 Claude 目录…":"目录与菜单已关闭。");
   if(floatNavigation!=null)floatNavigation.Hide();
  }
  async Task ReadNavigation(){
   if(navigationBusy||closed||testWindow!=IntPtr.Zero||profile!="claude"||navigationEnabled.IsChecked!=true)return;
   navigationBusy=true;int generation=navigationEpoch;
   try{
    ClaudeChromeSnapshot snapshot=await Task.Run(()=>ClaudeChromeReader.Read());
    if(!closed&&generation==navigationEpoch&&profile=="claude"&&navigationEnabled.IsChecked==true)navigationTranslator.Observe(snapshot);
   }catch(Exception){if(!closed&&generation==navigationEpoch)navigationTranslator.Reset("暂时无法读取目录，正在自动重试…");}
   finally{navigationBusy=false;}
  }
  static Rect NavigationDip(Rect value,double scale){return value.IsEmpty?Rect.Empty:new Rect(value.X/scale,value.Y/scale,value.Width/scale,value.Height/scale);}
  void ApplyNavigation(){
   if(closed||navigationTranslator==null||navigationView==null)return;
   ClaudeChromeSnapshot snapshot=navigationTranslator.Current;
   bool menuOpen=snapshot!=null&&snapshot.MenuVisible;
   bool settingsOpen=snapshot!=null&&snapshot.SettingsVisible;
   if(settingsOpen)navigationView.UpdateSettings(navigationTranslator.SettingsRows,navigationTranslator.Status);
   else navigationView.Update(navigationTranslator.SidebarRows,navigationTranslator.MenuRows,navigationTranslator.Status,menuOpen);
   if(!floatingMode||profile!="claude"||navigationEnabled.IsChecked!=true||snapshot==null||!snapshot.Connected||!snapshot.TargetForeground||(!snapshot.SidebarVisible&&!menuOpen&&!settingsOpen)){
    if(floatNavigation!=null)floatNavigation.Hide();return;
   }
   if(floatNavigation==null){floatNavigation=new FloatingNavigationWindow();if(inspectFloating)floatNavigation.ShowInTaskbar=true;floatNavigation.FullMode.Click+=(s,e)=>{navigationEnabled.IsChecked=false;};}
   double scale=1;PresentationSource source=PresentationSource.FromVisual(this);if(source!=null&&source.CompositionTarget!=null)scale=source.CompositionTarget.TransformToDevice.M11;
   Rect area=snapshot.WorkArea.IsEmpty?SystemParameters.WorkArea:NavigationDip(snapshot.WorkArea,scale);
   Rect targetBounds=NavigationDip(snapshot.WindowBounds,scale),sidebarBounds=NavigationDip(snapshot.SidebarBounds,scale);
   if(settingsOpen){
    if(floatInput!=null){StopVoice();floatInput.Hide();floatRead.Hide();}
    Rect settingsBounds=NavigationDip(snapshot.SettingsBounds,scale);
    if(floatNavigation.Follow.IsChecked==true){
     Rect placement=SettingsPlacement.Find(settingsBounds,area);
     if(placement.IsEmpty){floatNavigation.Hide();connection.Text="设置旁空间不足：请缩窄 Claude 窗口，或在语桥完整窗口查看中文。";return;}
     floatNavigation.MinWidth=160;floatNavigation.MinHeight=180;
     floatNavigation.Width=placement.Width;floatNavigation.Height=placement.Height;
     floatNavigation.Left=placement.Left;floatNavigation.Top=placement.Top;
    }
    floatNavigation.UpdateSettingsAligned(navigationTranslator.SettingsRows,navigationTranslator.Status,settingsBounds,NavigationDip(snapshot.SettingsContentBounds,scale),scale,snapshot.SettingsPageChinese);
    if(!floatNavigation.IsVisible)floatNavigation.Show();return;
   }
   if(floatNavigation.Follow.IsChecked==true){
    floatNavigation.MinWidth=Math.Min(160,area.Width);floatNavigation.MinHeight=Math.Min(260,area.Height);
    floatNavigation.Width=Math.Min(floatNavigation.Width,area.Width);
    floatNavigation.Top=Clamp(targetBounds.Top+6,area.Top,area.Bottom-floatNavigation.MinHeight);
    floatNavigation.Height=Math.Min(Math.Max(floatNavigation.MinHeight,sidebarBounds.Bottom-floatNavigation.Top),area.Bottom-floatNavigation.Top);
    floatNavigation.Left=Clamp(sidebarBounds.Right+8,area.Left,area.Right-floatNavigation.Width);
   }
   floatNavigation.UpdateAligned(navigationTranslator.SidebarRows,navigationTranslator.MenuRows,navigationTranslator.Status,menuOpen,sidebarBounds,NavigationDip(snapshot.MenuBounds,scale),scale);
   if(!floatNavigation.IsVisible)floatNavigation.Show();
  }
  void SyncFloating(){
   if(floatInput==null||closed||syncFloating)return;syncFloating=true;
   try{
    if(floatInput.Input.Text!=outgoing.Source.Text)floatInput.Input.Text=outgoing.Source.Text;
    floatInput.Follow.Content="跟随 "+TargetName();floatRead.Follow.Content="跟随 "+TargetName();
    floatInput.Input.IsReadOnly=filling&&!automaticFilling;UpdateVoiceButton(floatInput.Voice);
    floatInput.AutoFill.IsChecked=autoFill.IsChecked;floatInput.AutoFill.IsEnabled=profile=="claude";
    var choices=new List<Choice>();
    if(!String.IsNullOrWhiteSpace(CurrentTranslation)&&outgoing.Done==outgoing.Seen)choices.Add(new Choice{Text=CurrentTranslation,Provider="推荐"+LanguageName});
    if(conversationLanguage=="ja"&&alternativeSource==outgoing.Seen&&!String.IsNullOrWhiteSpace(alternativeText)&&alternativeText.Trim()!=outgoing.Second.Text.Trim())choices.Add(new Choice{Text=alternativeText,Provider="另一种措辞 · Google"});
    string signature=String.Join("\u001f",choices.ConvertAll(c=>c.Provider+"\u001e"+c.Text).ToArray());
    if(candidateSignature!=signature){
     Choice previous=floatInput.Candidates.SelectedItem as Choice;candidateSignature=signature;
     floatInput.Candidates.ItemsSource=choices;int selection=0;
     if(previous!=null){for(int i=0;i<choices.Count;i++)if(choices[i].Text==previous.Text)selection=i;}
     floatInput.Candidates.SelectedIndex=choices.Count==0?-1:selection;
    }
    floatInput.Fill.IsEnabled=CanFillTranslation()&&floatInput.Candidates.SelectedItem!=null;
    floatInput.Status.Text=(voiceError?VoiceStatusText()+"\n":"")+(sendBusy||awaitingSendReceipt||requestedSendVersion>=0?fillStatus.Text:filling?"正在自动填入译文…":CanFillTranslation()||outgoing.Source.Text.Length==0?fillStatus.Text:outgoing.Status.Text);
    floatInput.Voice.ToolTip=VoiceStatusText();
    RefreshHistory();floatRead.Japanese.Text=incoming.Source.Text;
    floatRead.Status.Text=connected?incoming.Status.Text:connection.Text;
   }finally{syncFloating=false;}
  }
  async Task RunAlternative(string text,int version,int generation,CancellationToken token){
   try{
    TranslationResult result=await service.TranslateAlternativeJapaneseAsync(text,token);
    if(closed||version!=outgoing.Version||generation!=epoch||token.IsCancellationRequested)return;
    alternativeText=result.Text;alternativeProvider=result.Provider;alternativeSource=text;
    alternativeStatus=alternativeText.Trim()==outgoing.Second.Text.Trim()?"两个服务给出了相同日文，已合并。":"Alt + ↑ / ↓ 选择候选，选中的日文会自动填入，不发送。";
   }catch(OperationCanceledException){}catch(Exception){if(!closed&&version==outgoing.Version&&generation==epoch)alternativeStatus="第二候选暂不可用，已完成的推荐日文仍可使用。";}
   finally{if(!closed)SyncFloating();}
  }
  async Task PlaceFloating(){
   if(!floatingMode||placing||closed)return;placing=true;
   try{
    if(navigationTranslator.Current!=null&&navigationTranslator.Current.SettingsVisible){StopVoice();floatInput.Hide();floatRead.Hide();var nav=navigationTranslator.Current;if(nav.TargetForeground){double scale=1;PresentationSource visual=PresentationSource.FromVisual(this);if(visual!=null&&visual.CompositionTarget!=null)scale=visual.CompositionTarget.TransformToDevice.M11;PlacePanelControls(NavigationDip(nav.WindowBounds,scale),NavigationDip(nav.WorkArea,scale));}else if(panelControls!=null)panelControls.Hide();return;}
    double callerScale=1.0;PresentationSource source=PresentationSource.FromVisual(this);if(source!=null&&source.CompositionTarget!=null)callerScale=source.CompositionTarget.TransformToDevice.M11;
    string placingProfile=profile;ClaudePlacement p=await Task.Run(()=>placingProfile=="claude"?FloatingPlacement.Read(callerScale):GenericDesktopAdapter.ReadPlacement(placingProfile,callerScale));if(!floatingMode||closed||placingProfile!=profile)return;
    if(!p.Available||!p.TargetForeground){if(systemVoiceRequested&&(WindowsVoiceInput.IsSystemInputForeground()||(DateTime.UtcNow-voiceRequestedAt).TotalSeconds<2)){floatRead.Hide();return;}StopVoice();floatInput.Hide();floatRead.Hide();if(panelControls!=null)panelControls.Hide();return;}
    Rect area=p.WorkArea.IsEmpty?SystemParameters.WorkArea:p.WorkArea;
    PlacePanelControls(p.WindowBounds,area);
    if(floatInput.Follow.IsChecked==true||!floatInput.IsVisible){
     if(floatInput.Follow.IsChecked==true){floatInput.Left=Clamp(p.InputBounds.Left,area.Left,area.Right-floatInput.Width);floatInput.Top=Clamp(p.InputBounds.Top-floatInput.Height-12,Math.Max(area.Top,p.WindowBounds.Top+40),area.Bottom-floatInput.Height);}
    }
    if(floatInput.Follow.IsChecked==true&&floatNavigation!=null&&floatNavigation.IsVisible){
     Rect inputBox=new Rect(floatInput.Left,floatInput.Top,floatInput.Width,floatInput.Height),navigationBox=new Rect(floatNavigation.Left,floatNavigation.Top,floatNavigation.Width,floatNavigation.Height);
     if(inputBox.IntersectsWith(navigationBox)){
      if(navigationBox.Right+8+floatInput.Width<=area.Right)floatInput.Left=navigationBox.Right+8;
      else if(navigationBox.Left-8-floatInput.Width>=area.Left)floatInput.Left=navigationBox.Left-8-floatInput.Width;
     }
    }
    if(floatRead.Follow.IsChecked==true){
     Point reading=FloatingLayout.ReadingPosition(p.WindowBounds,new Rect(floatInput.Left,floatInput.Top,floatInput.Width,floatInput.Height),new Size(floatRead.Width,floatRead.Height),area);
     floatRead.Left=reading.X;floatRead.Top=reading.Y;
     if(panelControls!=null){
      Rect controlBox=new Rect(panelControls.Left,panelControls.Top,panelControls.Width,panelControls.Height);
      if(new Rect(floatRead.Left,floatRead.Top,floatRead.Width,floatRead.Height).IntersectsWith(controlBox))
       floatRead.Top=Clamp(controlBox.Bottom+8,area.Top,area.Bottom-floatRead.Height);
     }
    }
    if(inputShown.IsChecked==true){if(!floatInput.IsVisible)floatInput.Show();}else floatInput.Hide();
    if(readingShown.IsChecked==true){if(!floatRead.IsVisible)floatRead.Show();}else floatRead.Hide();SyncFloating();
   }catch(Exception){if(!closed&&floatInput!=null){StopVoice();floatInput.Hide();floatRead.Hide();}}
   finally{placing=false;}
  }
  static double Clamp(double value,double minimum,double maximum){return Math.Max(minimum,Math.Min(value,Math.Max(minimum,maximum)));}
  bool IsInitialClaudeTransition(string before,string after){
   if(profile!="claude"||!newConversationTransitionAvailable)return false;
   string[] previous=before.Split(new[]{'|'},3),current=after.Split(new[]{'|'},3);Uri previousUrl,currentUrl;
   if(previous.Length!=3||current.Length!=3||previous[0]!="claude"||current[0]!="claude"||previous[1]!=current[1]||!Uri.TryCreate(previous[2],UriKind.Absolute,out previousUrl)||!Uri.TryCreate(current[2],UriKind.Absolute,out currentUrl))return false;
   return previousUrl.GetLeftPart(UriPartial.Authority)==currentUrl.GetLeftPart(UriPartial.Authority)&&previousUrl.AbsolutePath.TrimEnd('/')=="/new"&&Regex.IsMatch(currentUrl.AbsolutePath,@"^/chat/[^/]+/?$");
  }
  async Task Tick(){
   if(busy||closed||active.IsChecked!=true)return;busy=true;int generation=epoch;
   try{
    if(navigationTranslator.Current!=null&&navigationTranslator.Current.SettingsVisible){connected=false;connection.Text="正在查看 Claude 设置 · 关闭设置后恢复对话翻译";UpdateFillState();return;}
    DateTime readStarted=DateTime.UtcNow;bool wasFilling=filling;ChatWindowReader currentReader=reader;ChatSnapshot snapshot=await Task.Run(()=>currentReader.Read());if(closed||generation!=epoch)return;
    if(!snapshot.Connected){MarkDisconnected(snapshot.Status);await Schedule(outgoing,true);return;}
    string conversationKey=String.IsNullOrEmpty(snapshot.ConversationKey)?"title:"+(snapshot.Title??""):snapshot.ConversationKey;
    if(boundConversationKey!=""&&boundConversationKey!=conversationKey){
     if(IsInitialClaudeTransition(boundConversationKey,conversationKey)){MigrateConversation(boundConversationKey,conversationKey);newConversationTransitionAvailable=false;}
     else{suppressActiveEvents=true;try{active.IsChecked=false;}finally{suppressActiveEvents=false;}Pause("已切换聊天 · 请点“重新连接”继续");return;}
    }
    boundConversationKey=conversationKey;boundTitle=snapshot.Title;connected=true;connection.Text="已连接："+snapshot.Title;
    latestSnapshot=snapshot;
    if(profile=="claude"&&!wasFilling&&!filling&&readStarted>=lastVerifiedFill)ObserveConversation(snapshot);
    if(profile=="claude"&&!wasFilling&&!filling&&readStarted>=lastVerifiedFill)automaticDraft.Observe(conversationKey,snapshot.Draft);
    if(!baseline){lastReplyId=snapshot.ReplyId;lastReply=snapshot.Reply;baseline=true;RestoreWaitingStatus(outgoing,false);RestoreWaitingStatus(incoming,true);Observe(incoming,snapshot.Reply??"",true,snapshot.ReplyId??"");}
    if(snapshot.ReplyId!=lastReplyId||snapshot.Reply!=lastReply){lastReplyId=snapshot.ReplyId;lastReply=snapshot.Reply;Observe(incoming,snapshot.Reply??"",true,snapshot.ReplyId??"");}
    await Schedule(outgoing,true);await Schedule(incoming,false);
    if(!wasFilling&&readStarted>=lastVerifiedFill)await TryAutomaticFill(snapshot);
    await TryRequestedSend(snapshot);
   }catch(Exception){if(!closed&&generation==epoch){MarkDisconnected("暂时无法读取窗口，正在等待恢复…");Schedule(outgoing,true).GetAwaiter().GetResult();}}
   finally{busy=false;if(!closed)UpdateFillState();}
  }
  void Observe(Lane lane,string text,bool receive,string id=""){if(lane.Seen==text&&(!receive||lane.Id==id))return;Cancel(lane);lane.Id=id;lane.Seen=text;lane.Done="";lane.Changed=DateTime.UtcNow;if(lane.Source.Text!=text){updatingSource=true;try{lane.Source.Text=text;}finally{updatingSource=false;}}lane.First.Text="";if(lane.Second!=null)lane.Second.Text="";lane.Status.Text=text.Length==0?(receive?WaitingReply:"在上方输入中文，停顿后自动翻译…"):"检测到文字变化，等待片刻…";if(!receive&&fillStatus!=null&&!filling)fillStatus.Text=String.IsNullOrWhiteSpace(text)?"输入中文后自动填入所选译文；回车发送，Shift + Enter 换行。":"中文已更新；等待当前所选译文完成后再填入。";UpdateFillState();}
  Task Schedule(Lane lane,bool send){
   if(send&&(mainComposing||(floatInput!=null&&floatInput.IsComposing)))return Task.FromResult(0);
   if(lane.Seen==lane.Done||String.IsNullOrWhiteSpace(lane.Seen)||(DateTime.UtcNow-lane.Changed).TotalMilliseconds<650)return Task.FromResult(0);
   if(lane.Seen.Length>5000){lane.Done=lane.Seen;lane.Status.Text="原文超过 5000 字，本次暂不翻译。";return Task.FromResult(0);}
   if(send&&Regex.IsMatch(lane.Seen,@"[\u3040-\u30ff]")){lane.Done=lane.Seen;lane.Status.Text="草稿已是日文，等待中文输入…";return Task.FromResult(0);}
   if(send&&!Regex.IsMatch(lane.Seen,@"[\u3400-\u9fff]")){lane.Done=lane.Seen;lane.Status.Text="等待中文输入…";return Task.FromResult(0);}
   if(!send&&ConversationLanguages.ReplySource(lane.Seen,forceJapanese.IsChecked==true)==null){lane.Done=lane.Seen;lane.Status.Text="未检测到日文或英文回复；纯汉字日文可勾选按日文翻译。";return Task.FromResult(0);}
   lane.Done=lane.Seen;lane.Cancel=new CancellationTokenSource();int version=lane.Version,generation=epoch;string text=lane.Seen;var token=lane.Cancel.Token;DateTime changed=lane.Changed;lane.Status.Text="正在联网翻译…";
   lane.Pending=RunTranslation(lane,send,text,version,generation,changed,token);
   if(send&&floatingMode&&conversationLanguage=="ja"){alternativeStatus="第二种日文措辞正在翻译…";alternativePending=RunAlternative(text,version,generation,token);}
   return Task.FromResult(0);
  }
  async Task RunTranslation(Lane lane,bool send,string text,int version,int generation,DateTime changed,CancellationToken token){
   try{
    var one=TranslateField(lane,send,false,text,version,generation,token);
    Task<TranslationResult> two=send?TranslateField(lane,true,true,text,version,generation,token):null;
    // Observe both results even if either target fails. No abandoned second-task exceptions.
    await Task.WhenAll(two==null?new[]{one}:new[]{one,two});
    TranslationResult first=one.Result;TranslationResult second=two==null?null:two.Result;
    if(closed||version!=lane.Version||generation!=epoch||token.IsCancellationRequested)return;
    if(send&&(conversationLanguage=="en"?first==null:second==null)||!send&&first==null)return;
    double seconds=(DateTime.UtcNow-changed).TotalSeconds;string providers=first==null?second.Provider:second==null||second.Provider==first.Provider?first.Provider:first.Provider+" / "+second.Provider;lane.Status.Text=String.Format("已更新 · {0:0.0} 秒（检测到变化起） · {1}",seconds,providers)+(send&&(first==null||second==null)?" · 另一语言译文暂不可用":"");
    if(diagnosticFile!=null){try{string directory=Path.GetDirectoryName(diagnosticFile);if(!String.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);File.AppendAllText(diagnosticFile,String.Format("{0:o}\t{1}\t{2}\t{3:0.000}\t{4}\n",DateTime.Now,send?"draft":"reply",text.Length,seconds,providers));}catch(Exception){lane.Status.Text+=" · 诊断文件未能写入";}}
   }catch(OperationCanceledException){}catch(Exception ex){if(!closed&&version==lane.Version&&generation==epoch){lane.Status.Text=ex is InvalidOperationException?ex.Message:"翻译暂时失败，正在等待新的文字；也可重新连接。";}}
   finally{if(!closed)UpdateFillState();}
  }
  async Task<TranslationResult> TranslateField(Lane lane,bool send,bool japanese,string text,int version,int generation,CancellationToken token){
   try{
    var result=await service.TranslateAsync(text,send?"zh-Hans":ConversationLanguages.ReplySource(text,forceJapanese.IsChecked==true)??"auto",japanese?"ja":send?"en":"zh-Hans",token);
    if(closed||version!=lane.Version||generation!=epoch||token.IsCancellationRequested)return null;
    if(japanese){lane.Second.Text=result.Text;if(conversationLanguage=="ja")fillStatus.Text="日文已就绪，准备自动填入 Claude，不发送。";}
    else {lane.First.Text=result.Text;if(send&&conversationLanguage=="en")fillStatus.Text="英文已就绪，准备自动填入 Claude，不发送。";if(!send){conversationHistory.Reply(boundConversationKey,lane.Id,latestSnapshot==null?0:latestSnapshot.ReplyOrder,result.Text);RefreshHistory();}}
    UpdateFillState();return result;
   }catch(OperationCanceledException){return null;}
    catch(Exception ex){if(!closed&&version==lane.Version&&generation==epoch&&(!send||(conversationLanguage=="ja"?japanese:!japanese)))lane.Status.Text=ex is InvalidOperationException?ex.Message:"翻译暂时失败；修改中文或重新连接后重试。";return null;}
  }
 }
}
