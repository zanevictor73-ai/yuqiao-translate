using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Yuqiao {
 public static class ConversationLanguages {
  public static string Normalize(string language){return language=="en"?"en":"ja";}
  public static string Name(string language){return language=="en"?"英文":"日文";}
  // Kana identifies Japanese even inside a mixed/code-heavy reply. Latin text
  // is accepted independently of the outgoing choice, so either language reads
  // correctly in an existing or mixed-language conversation.
  public static string ReplySource(string text,bool forceJapanese){
   text=text??"";
   if(forceJapanese||Regex.IsMatch(text,@"[\u3040-\u30ff]"))return "ja";
   int latin=Regex.Matches(text,"[A-Za-z]").Count;
   int han=Regex.Matches(text,@"[\u3400-\u9fff]").Count;
   if(latin>=2&&latin>han&&Regex.IsMatch(text,"[A-Za-z]{2,}"))return "en";
   return null;
  }
 }

 public sealed partial class BridgeWindow {
  string conversationLanguage="ja";
  ComboBox languageSelector;
  bool syncingLanguage;
  string LanguageName {get{return ConversationLanguages.Name(conversationLanguage);}}
  string CurrentTranslation {get{return conversationLanguage=="en"?outgoing.First.Text:outgoing.Second.Text;}}
  string WaitingReply {get{return "等待当前"+LanguageName+"回复…";}}
  bool IsCurrentCandidate(string text){
   return !String.IsNullOrWhiteSpace(text)&&(text==CurrentTranslation||
    (conversationLanguage=="ja"&&alternativeSource==outgoing.Seen&&text==alternativeText));
  }
  void InitializeLanguageUi(WrapPanel controls){
   if(testWindow==IntPtr.Zero)conversationLanguage=ConversationLanguages.Normalize(AppPreferences.Load().ConversationLanguage);
   var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,12,4)};
   row.Children.Add(new TextBlock{Text="发送语言",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
   languageSelector=new ComboBox{Width=152,Padding=new Thickness(5,3,5,3)};
   languageSelector.Items.Add("日文 / Japanese");languageSelector.Items.Add("英文 / English");
   AutomationProperties.SetName(languageSelector,"发送语言");
   languageSelector.SelectedIndex=conversationLanguage=="en"?1:0;
   languageSelector.SelectionChanged+=(s,e)=>{if(!syncingLanguage)SetConversationLanguage(languageSelector.SelectedIndex==1?"en":"ja");};
   row.Children.Add(languageSelector);controls.Children.Insert(0,row);
   if(String.IsNullOrWhiteSpace(incoming.Seen))incoming.Status.Text=WaitingReply;
   SyncLanguageUi();
  }
  void SetConversationLanguage(string language){
   language=ConversationLanguages.Normalize(language);
   if(language==conversationLanguage){SyncLanguageUi();return;}
   if(filling||sendBusy||awaitingSendReceipt||systemVoiceRequested||mainComposing||(floatInput!=null&&floatInput.IsComposing)){
    fillStatus.Text="请先完成当前填入、发送或听写，再切换发送语言。";SyncLanguageUi();return;
   }
   conversationLanguage=language;
   // Cancel stale translations and any deferred Enter. Keep ownership of the
   // previous verified composer draft, so only our own draft can be replaced.
   requestedSendVersion=-1;epoch++;Cancel(outgoing);Cancel(incoming);
   outgoing.Done="";outgoing.First.Text="";outgoing.Second.Text="";outgoing.Changed=DateTime.UtcNow;
   incoming.Done="";incoming.First.Text="";incoming.Changed=DateTime.UtcNow;
   incoming.Status.Text=String.IsNullOrWhiteSpace(incoming.Seen)?WaitingReply:"正在重新翻译当前回复…";
   if(language=="en")forceJapanese.IsChecked=false;
   automaticDraft.InputChanged();candidateSignature="";
   fillStatus.Text="已切换为"+LanguageName+"；当前中文将重新翻译并自动填入，不发送。";
   if(testWindow==IntPtr.Zero)try{var preferences=AppPreferences.Load();preferences.ConversationLanguage=language;preferences.Save();}
    catch(Exception){fillStatus.Text+=" 本次未能保存语言选择。";}
   SyncLanguageUi();UpdateFillState();
  }
  void SyncLanguageUi(){
   syncingLanguage=true;
   try{
    int selected=conversationLanguage=="en"?1:0;
    bool enabled=!filling&&!sendBusy&&!awaitingSendReceipt&&!systemVoiceRequested&&!mainComposing&&(floatInput==null||!floatInput.IsComposing);
    if(languageSelector!=null){languageSelector.SelectedIndex=selected;languageSelector.IsEnabled=enabled;}
    if(autoFill!=null){autoFill.Content="自动填入"+LanguageName;autoFill.ToolTip="仅更新助手自己写入的译文；不会自动发送。";}
    if(forceJapanese!=null)forceJapanese.IsEnabled=conversationLanguage=="ja";
    if(floatInput!=null){
     floatInput.LanguageSelector.SelectedIndex=selected;floatInput.LanguageSelector.IsEnabled=enabled;
     floatInput.EmptyHint.Text="输入中文后，"+LanguageName+"候选会显示在这里";
     floatInput.Fill.Content="填入"+LanguageName;
     floatInput.Fill.ToolTip="Ctrl + Enter 填入选中的"+LanguageName+"；不会发送。";
     floatInput.Input.ToolTip="Enter：发送"+LanguageName+"；Shift + Enter：换行。发送确认后清空中文。";
     floatInput.Candidates.ToolTip="点击选择"+LanguageName+"候选；Alt + ↑ / ↓ 循环选择。";
     AutomationProperties.SetName(floatInput.Candidates,LanguageName+"候选译文");
     AutomationProperties.SetName(floatInput.AutoFill,"自动填入"+LanguageName);
    }
    if(floatRead!=null){floatRead.Original.Header="查看回复原文";AutomationProperties.SetName(floatRead.Japanese,"当前回复原文");}
   }finally{syncingLanguage=false;}
  }
 }
}
