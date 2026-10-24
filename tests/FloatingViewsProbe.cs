using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Yuqiao;

internal static class FloatingViewsProbe
{
    private static readonly List<string> output = new List<string>();
    private static int failed;
    private static void Check(string name,bool value){output.Add((value?"PASS":"FAIL")+"\t"+name);if(!value)failed++;}
    [STAThread] public static int Main(string[] args)
    {
        FloatingInputWindow input=null;FloatingReadWindow read=null;
        try
        {
            input=new FloatingInputWindow();read=new FloatingReadWindow();
            Check("input view compact and nonactivating",input.Width==480&&input.Height==290&&input.Topmost&&!input.ShowActivated&&!input.ShowInTaskbar&&!input.IsVisible);
            Check("editable local Chinese and disabled initial fill",!input.Input.IsReadOnly&&!input.Fill.IsEnabled&&input.Follow.IsChecked==true);
            Check("candidate font and wrapping template",input.Candidates.FontSize==16&&input.Candidates.ItemTemplate!=null&&input.Candidates.ItemContainerStyle!=null);
            MethodInfo move=typeof(FloatingInputWindow).GetMethod("MoveCandidate",BindingFlags.Instance|BindingFlags.NonPublic);
            move.Invoke(input,new object[]{1});Check("empty candidate navigation safe",input.Candidates.SelectedIndex==-1);
            input.Candidates.Items.Add("Microsoft · 明日の午後に会いましょう。");input.Candidates.Items.Add("Google · 明日の午後、お会いしましょう。");
            move.Invoke(input,new object[]{1});Check("candidate initial down selects first",input.Candidates.SelectedIndex==0);
            move.Invoke(input,new object[]{1});Check("candidate down selects second",input.Candidates.SelectedIndex==1);
            move.Invoke(input,new object[]{1});Check("candidate down wraps to first",input.Candidates.SelectedIndex==0);
            move.Invoke(input,new object[]{-1});Check("candidate up wraps to last",input.Candidates.SelectedIndex==1);
            TextComposition composition=new TextComposition(InputManager.Current,input.Input,"pin");
            input.Input.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,composition){RoutedEvent=TextCompositionManager.PreviewTextInputStartEvent});
            Check("IME start marks composing",input.IsComposing);
            input.Input.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,composition){RoutedEvent=TextCompositionManager.PreviewTextInputUpdateEvent});
            Check("IME update keeps composing",input.IsComposing);
            input.Input.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,composition){RoutedEvent=TextCompositionManager.PreviewTextInputEvent});
            Check("IME completed clears composing",!input.IsComposing);
            Check("reading view has readonly Chinese and folded Japanese",read.Chinese.IsReadOnly&&read.Japanese.IsReadOnly&&!read.Original.IsExpanded&&read.Follow.IsChecked==true&&read.Width==350&&read.Height==360&&!read.IsVisible);
            int inputReturns=0,readReturns=0;
            input.HideRequested+=delegate{inputReturns++;};read.HideRequested+=delegate{readReturns++;};
            input.Input.Text="保留中文";read.Chinese.Text="保留阅读译文";
            input.Close();read.Close();
            Check("normal close requests independent hide",inputReturns==1&&readReturns==1);
            Check("normal close retains text",input.Input.Text=="保留中文"&&read.Chinese.Text=="保留阅读译文");
            Check("no visible or external window operations",!input.IsVisible&&!read.IsVisible);
        }
        catch(Exception ex){failed++;output.Add("FAIL\tUnexpected exception\t"+ex);}
        finally
        {
            if(input!=null)input.ClosePermanently();if(read!=null)read.ClosePermanently();
            output.Add("RESULT\t"+(failed==0?"ALL PASSED":"FAILED")+"\tFailures="+failed);
            File.WriteAllLines(args[0],output,System.Text.Encoding.UTF8);foreach(string line in output)Console.WriteLine(line);
        }
        return failed==0?0:1;
    }
}
