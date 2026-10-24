using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Yuqiao {
 public sealed class PanelPreferences {
  public bool Input=true,Reading=true,Navigation=true;
  static string PathName {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YuqiaoTranslateStandalone","panels.json");}}
  public static PanelPreferences Load(){try{if(File.Exists(PathName)&&new FileInfo(PathName).Length<2048)return new JavaScriptSerializer().Deserialize<PanelPreferences>(File.ReadAllText(PathName,Encoding.UTF8))??new PanelPreferences();}catch(Exception){}return new PanelPreferences();}
  public void Save(){string path=PathName;Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
 }
 public sealed class FloatingPanelControls : Window {
  public readonly CheckBox InputToggle,ReadToggle,NavigationToggle;
  public readonly Button MainButton;
  public FloatingPanelControls(){
   FloatingUi.Configure(this,"语桥 · 小窗开关",330,42,330,42);ResizeMode=ResizeMode.NoResize;
   var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(9,0,8,0)};
   row.Children.Add(new TextBlock{Text="语桥",Foreground=FloatingUi.Accent,FontSize=12,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});
   NavigationToggle=Toggle("目录");InputToggle=Toggle("输入");ReadToggle=Toggle("对话");
   row.Children.Add(NavigationToggle);row.Children.Add(InputToggle);row.Children.Add(ReadToggle);
   MainButton=FloatingUi.SmallButton("主窗","打开完整翻译窗口");MainButton.FontSize=11;MainButton.Padding=new Thickness(7,3,7,3);row.Children.Add(MainButton);
   Content=new Border{Background=FloatingUi.Header,BorderBrush=FloatingUi.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Margin=new Thickness(3),Child=row};
  }
  static CheckBox Toggle(string text){var box=new CheckBox{Content=text,FontSize=12,IsChecked=true,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0),ToolTip="显示 / 隐藏"+text+"小窗"};System.Windows.Automation.AutomationProperties.SetName(box,text+"窗显示开关");return box;}
 }
}
