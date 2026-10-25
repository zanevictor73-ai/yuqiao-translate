using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Yuqiao {
 public sealed class ChatUserMessage {
  public string Id="",Text="";
  public int Order;
 }
 public sealed class SentDraft {
  public string Key,Chinese,Japanese;
  public int Version,BeforeCount;
  public bool EditedInClaude;
  public readonly HashSet<string> BeforeIds=new HashSet<string>();
 }
 // A disappearing composer is NOT a send receipt. Require a new matching user
 // message in the same conversation, as well as the submitted draft leaving it.
 public sealed class ConversationFlow {
  public SentDraft Draft {get;private set;}
  public void Written(ChatSnapshot before,string key,string chinese,string japanese,int version){
   var draft=new SentDraft{Key=key,Chinese=chinese,Japanese=japanese.Trim(),Version=version,BeforeCount=before.UserMessages.Count};
   foreach(var message in before.UserMessages)draft.BeforeIds.Add(message.Id);
   Draft=draft;
  }
  public void Move(string before,string after){if(Draft!=null&&Draft.Key==before)Draft.Key=after;}
  public void Suspend(){Draft=null;}
  public SentDraft Observe(ChatSnapshot snapshot,out ChatUserMessage sent){
   sent=null;var draft=Draft;
   if(draft==null||!snapshot.Connected||snapshot.ConversationKey!=draft.Key)return null;
   string current=(snapshot.Draft??"").Trim();
   if(snapshot.UserMessages.Count>draft.BeforeCount){
    foreach(var message in snapshot.UserMessages){
     if(!draft.BeforeIds.Contains(message.Id)&&SameText(message.Text,draft.Japanese)&&!SameText(current,draft.Japanese)){
      sent=message;Draft=null;return draft;
     }
    }
   }
   // Keep direct edits in Claude associated with this draft. The history labels
   // these as the Chinese original, rather than claiming an exact back-translation.
   if(snapshot.UserMessages.Count==draft.BeforeCount&&current.Length>0&&!SameText(current,draft.Japanese)){
    draft.Japanese=current;draft.EditedInClaude=true;
   }
   return null;
  }
  public static bool SameText(string one,string two){return Regex.Replace(one??"",@"\s+","")==Regex.Replace(two??"",@"\s+","");}
 }
 public sealed class ConversationDocument {
  public string Text="",LastUserId="";
  public int LastUserEnd;
 }
 public sealed class ConversationHistory {
  sealed class Entry {public string Id,Text,Label;public int Order;public bool Original;}
  readonly Dictionary<string,List<Entry>> conversations=new Dictionary<string,List<Entry>>();
  readonly Queue<string> keys=new Queue<string>();
  List<Entry> Get(string key){
   List<Entry> entries;if(conversations.TryGetValue(key,out entries))return entries;
   entries=new List<Entry>();conversations[key]=entries;keys.Enqueue(key);
   while(keys.Count>8)conversations.Remove(keys.Dequeue());return entries;
  }
  public void Move(string before,string after){
   List<Entry> entries;if(before==after||!conversations.TryGetValue(before,out entries))return;
   conversations.Remove(before);conversations[after]=entries;keys.Enqueue(after);
  }
  public bool HasUser(string key,string id){List<Entry> entries;if(!conversations.TryGetValue(key,out entries))return false;return entries.Exists(e=>e.Id=="u:"+id);}
  public void User(string key,ChatUserMessage message,string text,bool original,bool edited){
   Put(key,"u:"+message.Id,message.Order,text,original?(edited?"我 · 中文原稿（译文有手动改动）":"我 · 中文原稿"):"我 · 原文译回中文",original);
  }
  public void Reply(string key,string id,int order,string text){Put(key,"a:"+id,order,text,"Claude · 中文翻译",false);}
  void Put(string key,string id,int order,string text,string label,bool original){
   if(String.IsNullOrEmpty(key)||String.IsNullOrEmpty(text))return;
   var entries=Get(key);var entry=entries.Find(e=>e.Id==id);
   if(entry!=null&&entry.Original&&!original)return;
   if(entry==null){entry=new Entry{Id=id};entries.Add(entry);}
   entry.Text=text.Length>16000?text.Substring(0,16000)+"…":text;entry.Label=label;entry.Order=order;entry.Original=original;
   entries.Sort((a,b)=>a.Order.CompareTo(b.Order));while(entries.Count>60)entries.RemoveAt(0);
  }
  public string Render(string key){return Document(key).Text;}
  public ConversationDocument Document(string key){
   var document=new ConversationDocument();
   List<Entry> entries;if(!conversations.TryGetValue(key,out entries))return document;
   var result=new StringBuilder();
   foreach(var entry in entries){
    if(result.Length>0)result.AppendLine().AppendLine("────────────────────").AppendLine();
    result.AppendLine(entry.Label).Append(entry.Text);
    if(entry.Id.StartsWith("u:",StringComparison.Ordinal)){document.LastUserId=entry.Id;document.LastUserEnd=result.Length-(entry.Text.Length-entry.Text.TrimEnd().Length);}
   }
   document.Text=result.ToString();return document;
  }
 }
}
