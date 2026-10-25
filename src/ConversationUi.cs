using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yuqiao {
 public sealed partial class BridgeWindow {
  readonly ConversationFlow conversationFlow=new ConversationFlow();
  readonly ConversationHistory conversationHistory=new ConversationHistory();
  readonly Dictionary<string,DateTime> userTranslations=new Dictionary<string,DateTime>();
  ChatSnapshot latestSnapshot;
  TextBox historyText;
  ConversationReadingPosition historyPosition,floatingReadPosition;
  CheckBox inputShown,readingShown;
  PanelPreferences panelPreferences;
  FloatingPanelControls panelControls;
  bool syncingPanels,sendBusy,awaitingSendReceipt;
  int requestedSendVersion=-1;
  string requestedSendKey="";
  DateTime requestedSendAt,sentAt;
  Action<string,string,CancellationToken> explicitSender=ClaudeSend.Invoke;
  Action<string> targetFocuser=ClaudeSend.FocusComposer;
  Action<TextBox> systemVoiceEnder=WindowsVoiceInput.End;

  void InitializeConversationUi(WrapPanel controls,TabControl tabs){
   panelPreferences=testWindow==IntPtr.Zero?PanelPreferences.Load():new PanelPreferences();
   navigationEnabled.Content="目录窗";
   inputShown=new CheckBox{Content="输入窗",IsChecked=panelPreferences.Input,Margin=new Thickness(0,7,16,7)};
   readingShown=new CheckBox{Content="对话窗",IsChecked=panelPreferences.Reading,Margin=new Thickness(0,7,16,7)};
   controls.Children.Add(inputShown);controls.Children.Add(readingShown);
   navigationEnabled.IsChecked=panelPreferences.Navigation;
   inputShown.Checked+=(s,e)=>PanelVisibilityChanged();inputShown.Unchecked+=(s,e)=>PanelVisibilityChanged();
   readingShown.Checked+=(s,e)=>PanelVisibilityChanged();readingShown.Unchecked+=(s,e)=>PanelVisibilityChanged();
   navigationEnabled.Checked+=(s,e)=>PanelVisibilityChanged();navigationEnabled.Unchecked+=(s,e)=>PanelVisibilityChanged();
   historyText=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontSize=15,Padding=new Thickness(16),AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
   historyPosition=new ConversationReadingPosition(historyText);
   System.Windows.Automation.AutomationProperties.SetName(historyText,"双方中文对话记录");
   tabs.Items.Insert(1,new TabItem{Header="  中文对话记录  ",Content=historyText});
   outgoing.Source.PreviewKeyDown+=SourceShortcut;
   outgoing.Source.TextChanged+=(s,e)=>{
    if(requestedSendVersion>=0&&requestedSendVersion!=outgoing.Version)requestedSendVersion=-1;
   };
  }
  void SourceShortcut(object sender,KeyEventArgs e){
   if(e.Key==Key.Escape){systemVoiceRequested=false;UpdateFillState();return;}
   if(mainComposing||(floatInput!=null&&floatInput.IsComposing)||e.Key!=Key.Return||Keyboard.Modifiers!=ModifierKeys.None)return;
   e.Handled=true;if(!e.IsRepeat)RequestSend();
  }
  void RequestSend(){
   if(closed||sendBusy||awaitingSendReceipt)return;
   if(profile!="claude"||!connected||active.IsChecked!=true||String.IsNullOrWhiteSpace(outgoing.Source.Text)){
    fillStatus.Text="请先连接 Claude 并输入中文，再按回车发送。";UpdateFillState();return;
   }
   if(mainComposing||(floatInput!=null&&floatInput.IsComposing))return;
   if(systemVoiceRequested){
    try{systemVoiceEnder(LocalEditor());systemVoiceRequested=false;}
    catch(Exception ex){fillStatus.Text="请先结束系统听写："+ex.Message;UpdateFillState();return;}
   }
   requestedSendVersion=outgoing.Version;requestedSendKey=boundConversationKey;requestedSendAt=DateTime.UtcNow;
   outgoing.Changed=DateTime.UtcNow.AddSeconds(-1);
   fillStatus.Text="已收到回车；当前所选译文就绪后发送这一句。修改中文会取消本次发送。";UpdateFillState();
  }
  TextBox LocalEditor(){return floatingMode&&floatInput!=null&&floatInput.IsVisible?floatInput.Input:outgoing.Source;}
  string SelectedTranslation(){var choice=floatingMode&&floatInput!=null?floatInput.Candidates.SelectedItem as Choice:null;return choice==null?CurrentTranslation:choice.Text;}
  async Task TryRequestedSend(ChatSnapshot snapshot){
   if(requestedSendVersion<0||sendBusy||filling)return;
   if(requestedSendVersion!=outgoing.Version||requestedSendKey!=boundConversationKey||active.IsChecked!=true){requestedSendVersion=-1;return;}
   if((DateTime.UtcNow-requestedSendAt).TotalSeconds>20){requestedSendVersion=-1;fillStatus.Text="等待译文超时，未发送；中文已保留，请核对后再按回车。";return;}
   if(testWindow==IntPtr.Zero&&!ClaudeSend.IsInteractionForeground(boundConversationKey)){requestedSendVersion=-1;fillStatus.Text="已切换到其他软件，本次回车发送已取消。";return;}
   if(!CanFillTranslation()||ClaudeSend.IsEnterHeld())return;
   string japanese=SelectedTranslation(),key=boundConversationKey,chinese=outgoing.Source.Text;
   if(!IsCurrentCandidate(japanese))return;
   int version=outgoing.Version,generation=epoch;requestedSendVersion=-1;
   sendBusy=true;filling=true;fillStatus.Text="正在按你的回车指令发送译文…";UpdateFillState();
   using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(outgoing.Cancel==null?CancellationToken.None:outgoing.Cancel.Token)){
    try{
     await Task.Run(()=>automaticComposer(key,japanese,automaticDraft.Key==key?automaticDraft.OwnedText:null,cancel.Token));
     if(closed||generation!=epoch||version!=outgoing.Version)return;
     // Preserve the earlier receipt baseline when this is the same verified draft.
     if(conversationFlow.Draft==null||conversationFlow.Draft.Key!=key||conversationFlow.Draft.Version!=version||!ConversationFlow.SameText(conversationFlow.Draft.Japanese,japanese))
      conversationFlow.Written(snapshot,key,chinese,japanese,version);
     automaticDraft.RecordVerified(key,japanese);lastVerifiedFill=DateTime.UtcNow;
     await Task.Run(()=>explicitSender(key,japanese,cancel.Token));
     awaitingSendReceipt=true;sentAt=DateTime.UtcNow;
     fillStatus.Text="已执行回车发送，正在核对 Claude 中的新消息…";
     if(testWindow==IntPtr.Zero)await Task.Run(()=>targetFocuser(key));
    }catch(OperationCanceledException){fillStatus.Text="输入已变化，本次发送已取消，中文已保留。";}
     catch(Exception ex){fillStatus.Text=ex is InvalidOperationException?ex.Message:"发送尚未确认，中文已保留，请核对 Claude。";}
    finally{sendBusy=false;filling=false;if(!closed)UpdateFillState();}
   }
  }
  void ObserveConversation(ChatSnapshot snapshot){
   latestSnapshot=snapshot;
   ChatUserMessage sent;SentDraft receipt=conversationFlow.Observe(snapshot,out sent);
   if(receipt!=null){
    conversationHistory.User(snapshot.ConversationKey,sent,receipt.Chinese,true,receipt.EditedInClaude);
    awaitingSendReceipt=false;requestedSendVersion=-1;systemVoiceRequested=false;
    voiceHint="上一句已发送；点击语音输入可开始下一句。";
    if(outgoing.Version==receipt.Version&&outgoing.Source.Text==receipt.Chinese){
     automaticDraft.InputChanged();Observe(outgoing,"",false);
     fillStatus.Text="已发送；中文已记入对话窗，现在可以说下一句。";
    }else fillStatus.Text="上一句已发送并记入对话窗；你正在写的新中文已保留。";
   }
   if(awaitingSendReceipt&&(DateTime.UtcNow-sentAt).TotalSeconds>8)
    fillStatus.Text="尚未确认 Claude 收到消息；中文已保留，不会重复发送。请先核对 Claude。";
   if(snapshot.UserMessages.Count>0){
    var latest=snapshot.UserMessages[snapshot.UserMessages.Count-1];
    string id=snapshot.ConversationKey+"\u001f"+latest.Id;DateTime retry;
    if(!conversationHistory.HasUser(snapshot.ConversationKey,latest.Id)&&(!userTranslations.TryGetValue(id,out retry)||DateTime.UtcNow>=retry)){
     userTranslations[id]=DateTime.UtcNow.AddSeconds(30);
     TranslateObservedUser(snapshot.ConversationKey,latest);
    }
   }
   RefreshHistory();
  }
  async void TranslateObservedUser(string key,ChatUserMessage message){
   try{
    var result=await service.TranslateAsync(message.Text,ConversationLanguages.ReplySource(message.Text,false)??"auto","zh-Hans",CancellationToken.None);
    if(closed)return;
    conversationHistory.User(key,message,result.Text,false,false);RefreshHistory();
   }catch(Exception){}
  }
  void RefreshHistory(){
   var document=conversationHistory.Document(boundConversationKey);
   if(historyPosition!=null)historyPosition.Update(boundConversationKey,document);
   if(floatRead!=null){
    if(floatingReadPosition==null)floatingReadPosition=new ConversationReadingPosition(floatRead.Chinese);
    floatingReadPosition.Update(boundConversationKey,document.Text.Length==0?new ConversationDocument{Text=incoming.First.Text}:document);
   }
  }
  void MigrateConversation(string before,string after){
   conversationFlow.Move(before,after);conversationHistory.Move(before,after);
   if(historyPosition!=null)historyPosition.Move(before,after);if(floatingReadPosition!=null)floatingReadPosition.Move(before,after);
   if(requestedSendKey==before)requestedSendKey=after;
  }
  void ResetSendState(){requestedSendVersion=-1;awaitingSendReceipt=false;conversationFlow.Suspend();latestSnapshot=null;}
  void InitializePanelControls(){
   if(panelControls!=null)return;
   panelControls=new FloatingPanelControls();if(inspectFloating)panelControls.ShowInTaskbar=true;
   panelControls.MainButton.Click+=(s,e)=>ShowFullMode();
   panelControls.InputToggle.Checked+=(s,e)=>{if(!syncingPanels)inputShown.IsChecked=true;};panelControls.InputToggle.Unchecked+=(s,e)=>{if(!syncingPanels)inputShown.IsChecked=false;};
   panelControls.ReadToggle.Checked+=(s,e)=>{if(!syncingPanels)readingShown.IsChecked=true;};panelControls.ReadToggle.Unchecked+=(s,e)=>{if(!syncingPanels)readingShown.IsChecked=false;};
   panelControls.NavigationToggle.Checked+=(s,e)=>{if(!syncingPanels)navigationEnabled.IsChecked=true;};panelControls.NavigationToggle.Unchecked+=(s,e)=>{if(!syncingPanels)navigationEnabled.IsChecked=false;};
   SyncPanelControls();
  }
  void SyncPanelControls(){
   if(panelControls==null)return;syncingPanels=true;
   try{panelControls.InputToggle.IsChecked=inputShown.IsChecked;panelControls.ReadToggle.IsChecked=readingShown.IsChecked;panelControls.NavigationToggle.IsChecked=navigationEnabled.IsChecked;}finally{syncingPanels=false;}
  }
  void PanelVisibilityChanged(){
   if(inputShown==null||readingShown==null)return;
   if(inputShown.IsChecked!=true&&systemVoiceRequested){
    try{systemVoiceEnder(LocalEditor());systemVoiceRequested=false;}
    catch(Exception){inputShown.IsChecked=true;fillStatus.Text="请先结束 Windows 听写，再隐藏输入窗。";return;}
   }
   panelPreferences.Input=inputShown.IsChecked==true;panelPreferences.Reading=readingShown.IsChecked==true;panelPreferences.Navigation=navigationEnabled.IsChecked==true;
   if(!panelPreferences.Input&&floatInput!=null)floatInput.Hide();if(!panelPreferences.Reading&&floatRead!=null)floatRead.Hide();if(!panelPreferences.Navigation&&floatNavigation!=null)floatNavigation.Hide();
   SyncPanelControls();
   if(testWindow==IntPtr.Zero)try{panelPreferences.Save();}catch(Exception){footer.Text="小窗开关已生效；本次未能保存，下次可能恢复默认。";}
  }
  void PlacePanelControls(Rect window,Rect area){
   if(panelControls==null||!floatingMode)return;
   panelControls.Left=Clamp(window.Right-panelControls.Width-16,area.Left,area.Right-panelControls.Width);
   panelControls.Top=Clamp(window.Top+38,area.Top,area.Bottom-panelControls.Height);
   if(!panelControls.IsVisible)panelControls.Show();
  }
 }
}
