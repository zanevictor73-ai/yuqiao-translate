using System;
using System.Windows;
using System.Windows.Controls;

namespace Yuqiao {
 // Each reading surface owns its position. Only a new user message positions
 // its final line at the bottom; streamed answers never follow their own end.
 public sealed class ConversationReadingPosition {
  readonly TextBox box;
  string key=null,lastUserId="",text="";
  int lastUserEnd,spacerLines;
  int selectionStart,selectionLength;
  bool pending,anchor,applying,relativeToQuestion;
  double savedOffset,questionDistance;

  public ConversationReadingPosition(TextBox box){
   this.box=box;
   box.Loaded+=(s,e)=>ApplyPending();
   box.IsVisibleChanged+=(s,e)=>ApplyPending();
   box.SizeChanged+=(s,e)=>ApplyPending();
   box.LayoutUpdated+=(s,e)=>ApplyPending();
  }
  public void Move(string before,string after){if(key==before)key=after;}
  public void Update(string conversationKey,ConversationDocument document){
   bool changedConversation=key!=conversationKey;
   bool newQuestion=document.LastUserId.Length>0&&(changedConversation||document.LastUserId!=lastUserId);
   if(!changedConversation&&!newQuestion&&text==document.Text)return;
   if(!pending){
    selectionStart=box.SelectionStart;selectionLength=box.SelectionLength;
    savedOffset=box.VerticalOffset;relativeToQuestion=false;
    if(!changedConversation&&!newQuestion&&lastUserEnd>0&&box.ViewportHeight>0){
     Rect previous=box.GetRectFromCharacterIndex(spacerLines+Math.Min(lastUserEnd,text.Length)-1,true);
     if(!previous.IsEmpty){relativeToQuestion=true;questionDistance=-previous.Bottom;}
    }
   }
   if(changedConversation||newQuestion){spacerLines=0;savedOffset=0;anchor=newQuestion;relativeToQuestion=false;}
   key=conversationKey;lastUserId=document.LastUserId;text=document.Text;lastUserEnd=document.LastUserEnd;
   pending=true;
   applying=true;
   try{box.Text=new string('\n',spacerLines)+text;box.UpdateLayout();}
   finally{applying=false;}
   ApplyPending();
  }
  void ApplyPending(){
   if(!pending||applying||box.ActualWidth<=0||box.ViewportHeight<=0)return;
   applying=true;
   try{
    box.UpdateLayout();
    if(anchor&&lastUserEnd>0){
     // Empty space before a short first question lets it occupy the bottom
     // line too. Keep it stable while its answer grows and while the user reads.
     int index=spacerLines+Math.Min(lastUserEnd,text.Length)-1;
     Rect end=box.GetRectFromCharacterIndex(index,true);
     if(end.IsEmpty)return;
     double absoluteBottom=end.Bottom+box.VerticalOffset;
     double height=box.ViewportHeight+box.Padding.Top+box.BorderThickness.Top;
     if(absoluteBottom<height&&end.Height>0){
      int extra=(int)Math.Ceiling((height-absoluteBottom)/end.Height);
      spacerLines+=extra;
      box.Text=new string('\n',spacerLines)+text;box.UpdateLayout();
      end=box.GetRectFromCharacterIndex(spacerLines+Math.Min(lastUserEnd,text.Length)-1,true);
      if(end.IsEmpty)return;
      absoluteBottom=end.Bottom+box.VerticalOffset;
     }
     savedOffset=Math.Max(0,absoluteBottom-height);
    }else if(relativeToQuestion&&lastUserEnd>0){
     // History is bounded; removing old entries or completing an earlier
     // translation must not shift the answer currently being read.
     Rect end=box.GetRectFromCharacterIndex(spacerLines+Math.Min(lastUserEnd,text.Length)-1,true);
     if(!end.IsEmpty)savedOffset=Math.Max(0,end.Bottom+box.VerticalOffset+questionDistance);
    }
    // WPF may focus a newly selected tab after its first layout. Keep its
    // caret at the question too, so that focus cannot bring character zero
    // back into view after the explicit scroll has already completed.
    int start=anchor?spacerLines+Math.Min(lastUserEnd,text.Length):Math.Min(selectionStart,box.Text.Length);
    box.Select(start,anchor?0:Math.Min(selectionLength,box.Text.Length-start));
    box.ScrollToVerticalOffset(savedOffset);box.UpdateLayout();
    pending=false;anchor=false;
   }finally{applying=false;}
  }
 }
}
