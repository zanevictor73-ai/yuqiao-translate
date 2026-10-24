using System;

namespace Yuqiao {
 // Pure ownership policy. Only text verified as written by this session may
 // be replaced automatically. Clearing/sending a draft suppresses reinsertion
 // until the user changes local input (or explicitly chooses another candidate).
 public sealed class AutomaticDraft {
  public string Key {get;private set;}
  public string OwnedText {get;private set;}
  public string Status {get;private set;}
  public int Revision {get;private set;}
  int suppressed=-1;
  string attempt="";
  public AutomaticDraft(){Key="";OwnedText=null;Status="";}
  public void InputChanged(){Revision++;attempt="";Status="";}
  public void Suspend(){OwnedText=null;Key="";suppressed=Revision;attempt="";Status="自动填入已暂停；修改中文后继续。";}
  public void Observe(string key,string draft){
   draft=(draft??"").Trim();
   if(Key!=key){if(Key.Length>0)suppressed=Revision;Key=key;OwnedText=null;attempt="";}
   if(OwnedText!=null&&draft!=OwnedText){
    OwnedText=null;suppressed=Revision;attempt="";
    Status=draft.Length==0?"Claude 草稿已清空或发送，等待新的中文；不会重新填回旧内容。":"Claude 草稿被手动修改，已停止自动覆盖。";
   }
  }
  public bool TryPlan(string key,string draft,string desired,out string expected){
   expected=null;Observe(key,draft);draft=(draft??"").Trim();desired=(desired??"").Trim();
   if(String.IsNullOrWhiteSpace(key)||desired.Length==0||suppressed==Revision)return false;
   if(draft==desired){Status="日文已在 Claude 输入框中，尚未发送。";return false;}
   if(draft.Length>0&&(OwnedText==null||draft!=OwnedText)){
    Status="Claude 已有其他草稿，已保留；清空后可自动填入本次日文。";return false;
   }
   string signature=Revision+"\u001f"+key+"\u001f"+draft+"\u001f"+desired;
   if(attempt==signature)return false;
   attempt=signature;expected=OwnedText??"";return true;
  }
  public void RecordVerified(string key,string desired){if(Key==key){OwnedText=desired.Trim();Status="日文已自动填入 Claude，尚未发送。";}}
  public void RecordFailure(string status){Status=status;}
 }
}
