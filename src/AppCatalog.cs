using System;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Yuqiao {
 public sealed class SupportedApp {
  public readonly string Id,DisplayName,ProcessName,ReaderProfile;
  public readonly bool CanTranslate,CanFill;
  readonly string[] processNames;
  public string[] ProcessNames {get{return (string[])processNames.Clone();}}
  public SupportedApp(string id,string displayName,string processName,string readerProfile,bool canTranslate,bool canFill,params string[] aliases){
   Id=id;DisplayName=displayName;ProcessName=processName;ReaderProfile=readerProfile;
   CanTranslate=canTranslate;CanFill=canFill;
   processNames=new string[1+(aliases==null?0:aliases.Length)];processNames[0]=processName;
   if(aliases!=null)Array.Copy(aliases,0,processNames,1,aliases.Length);
  }
  public override string ToString(){return DisplayName;}
 }

 /// <summary>Built-in adapters; actual compatibility still needs app-version validation.</summary>
 public static class AppCatalog {
  static readonly SupportedApp[] apps={
   new SupportedApp("claude","Claude 客户端","claude","claude",true,true),
   new SupportedApp("chatgpt","ChatGPT 客户端（实验适配）","ChatGPT","chatgpt",true,false),
   new SupportedApp("deepseek","DeepSeek Harness","DeepSeek Harness","deepseek",true,true,"deepseek")
  };
  public static SupportedApp[] All {get{return (SupportedApp[])apps.Clone();}}
  public static SupportedApp Find(string id){
   if(id==null)return null;
   foreach(var app in apps)if(String.Equals(app.Id,id.Trim(),StringComparison.OrdinalIgnoreCase))return app;
   return null;
  }
  public static SupportedApp Get(string id){return Find(id)??apps[0];}
 }

 /// <summary>Only product settings are persisted; no message text or translations.</summary>
 public sealed class AppPreferences {
  public string SelectedAppId="claude";
  static readonly object fileLock=new object();
  public static string SettingsPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YuqiaoTranslateStandalone","settings.json");}}

  public static AppPreferences Load(){
   lock(fileLock){
    try{
     string path=SettingsPath;
     if(!File.Exists(path)||new FileInfo(path).Length>16384)return new AppPreferences();
     return ParseJson(File.ReadAllText(path,Encoding.UTF8));
    }catch(IOException){return new AppPreferences();}
     catch(UnauthorizedAccessException){return new AppPreferences();}
    }
  }

  internal static AppPreferences ParseJson(string json){
   try{
    if(String.IsNullOrWhiteSpace(json)||json.Length>16384)return new AppPreferences();
    var preferences=new JavaScriptSerializer{MaxJsonLength=16384}.Deserialize<AppPreferences>(json);
    if(preferences==null)return new AppPreferences();
    preferences.SelectedAppId=AppCatalog.Get(preferences.SelectedAppId).Id;
    return preferences;
   }catch(ArgumentException){return new AppPreferences();}
    catch(InvalidOperationException){return new AppPreferences();}
  }

  internal string ToJson(){
   return new JavaScriptSerializer().Serialize(new {
    SelectedAppId=AppCatalog.Get(SelectedAppId).Id
   });
  }

  /// <summary>Atomic same-directory replacement, serialized across manual application instances.</summary>
  public void Save(){
   lock(fileLock){
    string mutexName;
    using(var identity=WindowsIdentity.GetCurrent()){
     if(identity.User==null)throw new InvalidOperationException("无法识别当前 Windows 用户。");
     mutexName=@"Local\YuqiaoPreferences_"+identity.User.Value;
    }
    using(var mutex=new Mutex(false,mutexName)){
     bool acquired=false;
     try{
      try{acquired=mutex.WaitOne(3000);}catch(AbandonedMutexException){acquired=true;}
      if(!acquired)throw new IOException("设置正在由另一个语桥窗口保存，请稍后再试。");
      string path=SettingsPath;
      string directory=Path.GetDirectoryName(path);
      Directory.CreateDirectory(directory);
      string temporary=Path.Combine(directory,"settings-"+Guid.NewGuid().ToString("N")+".tmp");
      try{
       byte[] data=new UTF8Encoding(false).GetBytes(ToJson());
       using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){
        stream.Write(data,0,data.Length);stream.Flush(true);
       }
       if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
      }finally{
       try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
      }
     }finally{if(acquired)mutex.ReleaseMutex();}
    }
   }
  }
 }
}
