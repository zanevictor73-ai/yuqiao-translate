using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;

namespace Yuqiao
{
    public sealed class NavigationDisplayRow
    {
        public string Id="",Text="",Icon="",Kind="";
        public Rect Bounds=Rect.Empty;
    }
    // The caller supplies already-filtered UI chrome snapshots on the UI thread.
    // Only recent-title rows may use the network; fixed commands stay local.
    public sealed class NavigationTranslator : IDisposable
    {
        readonly TranslationService service;
        readonly Dictionary<string,string> cache=new Dictionary<string,string>(StringComparer.Ordinal);
        readonly Queue<string> cacheOrder=new Queue<string>();
        readonly Dictionary<string,DateTime> retryAfter=new Dictionary<string,DateTime>(StringComparer.Ordinal);
        readonly SemaphoreSlim limit=new SemaphoreSlim(2);
        CancellationTokenSource cancellation;
        Task pending;
        string signature="";
        int version;
        bool disposed;
        public ClaudeChromeSnapshot Current {get;private set;}
        public string SidebarText {get;private set;}
        public string MenuText {get;private set;}
        public string Status {get;private set;}
        public List<NavigationDisplayRow> SidebarRows {get;private set;}
        public List<NavigationDisplayRow> MenuRows {get;private set;}
        public List<NavigationDisplayRow> SettingsRows {get;private set;}
        public event Action Updated;

        public NavigationTranslator(TranslationService service){this.service=service;SidebarText="";MenuText="";SidebarRows=new List<NavigationDisplayRow>();MenuRows=new List<NavigationDisplayRow>();SettingsRows=new List<NavigationDisplayRow>();Status="等待 Claude 目录…";}
        public void Observe(ClaudeChromeSnapshot snapshot)
        {
            if(disposed)return;
            if(snapshot==null||!snapshot.Connected){Reset(snapshot==null?"等待 Claude 目录…":snapshot.Status);return;}
            string next=Signature(snapshot);
            Current=snapshot;
            if(next!=signature){Cancel();signature=next;}
            Render();
            if(snapshot.SettingsVisible)return;
            if(pending!=null&&!pending.IsCompleted)return;
            var missing=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
            if(snapshot.SidebarVisible)foreach(var item in snapshot.Sidebar)
            {
                if(!SafeTitle(item)||cache.ContainsKey(item.Original)||!seen.Add(item.Original))continue;
                DateTime retry;if(retryAfter.TryGetValue(item.Original,out retry)&&retry>DateTime.UtcNow)continue;
                missing.Add(item.Original);if(missing.Count>=20)break;
            }
            if(missing.Count==0)return;
            cancellation=new CancellationTokenSource();
            pending=TranslateTitles(missing,version,cancellation.Token);
        }
        public void Reset(string message)
        {
            Cancel();Current=null;signature="";SidebarText="";MenuText="";SidebarRows.Clear();MenuRows.Clear();SettingsRows.Clear();Status=message??"等待 Claude 目录…";Notify();
        }
        void Cancel(){version++;if(cancellation!=null){cancellation.Cancel();cancellation.Dispose();cancellation=null;}pending=null;}
        static bool SafeTitle(ClaudeChromeItem item)
        {
            return item!=null&&item.Kind=="recent-title"&&item.NeedsTranslation&&String.IsNullOrEmpty(item.LocalChinese)
                &&!String.IsNullOrWhiteSpace(item.Original)&&item.Original.Length<=160
                &&item.Original.IndexOf('@')<0&&item.Original.IndexOf('＠')<0
                &&!Regex.IsMatch(item.Original,@"[\p{Cc}]|://|(?:[\w-]+\.)+[A-Za-z]{2,}(?:\b|/)");
        }
        static string Signature(ClaudeChromeSnapshot snapshot)
        {
            var value=new StringBuilder(snapshot.WindowKey??"");value.Append('|').Append(snapshot.SidebarVisible).Append('|').Append(snapshot.MenuVisible);
            foreach(var item in snapshot.Sidebar)value.Append('|').Append(item.Kind).Append(':').Append(item.Original.Length).Append(':').Append(item.Original).Append(':').Append(item.LocalChinese);
            foreach(var item in snapshot.AccountMenu)value.Append('|').Append(item.Original.Length).Append(':').Append(item.Original).Append(':').Append(item.LocalChinese);
            value.Append('|').Append(snapshot.SettingsVisible).Append('|').Append(snapshot.SettingsPageKey);
            foreach(var item in snapshot.Settings)value.Append('|').Append(item.Kind).Append(':').Append(item.LocalChinese);
            return value.ToString();
        }
        async Task TranslateTitles(List<string> titles,int generation,CancellationToken token)
        {
            var tasks=new List<Task>();foreach(string title in titles)tasks.Add(TranslateOne(title,generation,token,limit));
            await Task.WhenAll(tasks);
        }
        async Task TranslateOne(string title,int generation,CancellationToken token,SemaphoreSlim limit)
        {
            bool entered=false;
            try
            {
                await limit.WaitAsync(token);entered=true;
                TranslationResult translated=await service.TranslateAsync(title,"auto","zh-Hans",token);
                if(disposed||generation!=version||token.IsCancellationRequested)return;
                if(!cache.ContainsKey(title)){cacheOrder.Enqueue(title);if(cacheOrder.Count>200)cache.Remove(cacheOrder.Dequeue());}
                cache[title]=translated.Text;retryAfter.Remove(title);Render();
            }
            catch(OperationCanceledException){}
            catch(Exception){if(!disposed&&generation==version&&!token.IsCancellationRequested){if(retryAfter.Count>=200)retryAfter.Clear();retryAfter[title]=DateTime.UtcNow.AddSeconds(20);Render();}}
            finally{if(entered)limit.Release();}
        }
        void Render()
        {
            if(disposed||Current==null)return;
            var lines=new List<string>();bool waiting=false,failed=false;
            SidebarRows=new List<NavigationDisplayRow>();MenuRows=new List<NavigationDisplayRow>();
            SettingsRows=new List<NavigationDisplayRow>();
            if(Current.SettingsVisible){
                SidebarText="";MenuText="";
                foreach(var item in Current.Settings){
                    if(String.IsNullOrWhiteSpace(item.LocalChinese)||(item.Kind!="settings-tab"&&item.Kind!="section"&&item.Kind!="setting"&&item.Kind!="description"))continue;
                    SettingsRows.Add(new NavigationDisplayRow{Id=item.Id,Text=item.LocalChinese,Icon="",Kind=item.Kind,Bounds=item.Bounds});
                }
                Status=(String.IsNullOrWhiteSpace(Current.SettingsPageChinese)?"设置":Current.SettingsPageChinese)+" · 中文按原行对应；未匹配选项请查看原页面。";Notify();return;
            }
            if(Current.SidebarVisible)foreach(var item in Current.Sidebar)
            {
                if(item.Kind=="section"&&item.LocalChinese=="导航")continue;
                string translated=item.LocalChinese;
                if(SafeTitle(item)&&!cache.TryGetValue(item.Original,out translated))
                {
                    DateTime retry;bool hasFailure=retryAfter.TryGetValue(item.Original,out retry)&&retry>DateTime.UtcNow;
                    translated=hasFailure?"暂时未译，稍后重试":"正在翻译…";waiting|=!hasFailure;failed|=hasFailure;
                }
                if(String.IsNullOrWhiteSpace(translated))translated="暂未适配";
                // Do not fall back to Japanese source or a service's untranslated
                // Japanese response in this explicitly Chinese-only view.
                if(Regex.IsMatch(translated,@"[\u3040-\u30ff\uff66-\uff9f]"))translated="标题暂未译出";
                lines.Add(translated);
                SidebarRows.Add(DisplayRow(item,translated));
            }
            SidebarText=Current.SidebarVisible?String.Join(Environment.NewLine,lines.ToArray()).Trim():"侧栏已收起；展开 Claude 左侧栏后显示。";
            lines.Clear();
            if(Current.MenuVisible)foreach(var item in Current.AccountMenu)
                if(!String.IsNullOrWhiteSpace(item.LocalChinese)){lines.Add(item.LocalChinese);MenuRows.Add(DisplayRow(item,item.LocalChinese));}
            MenuText=String.Join(Environment.NewLine,lines.ToArray());
            Status=Current.MenuVisible?"已同步头像菜单 · 中文仅作对照，请在 Claude 点击原菜单。":"目录自动同步 · 点击 Claude 头像可查看菜单中文。";
            if(waiting)Status+="\n会话标题正在联网翻译…";else if(failed)Status+="\n部分标题暂未翻译，稍后自动重试。";
            if(Current.MenuHasUnsupportedItems)Status+="\n当前菜单有尚未适配的项目。";
            Notify();
        }
        static NavigationDisplayRow DisplayRow(ClaudeChromeItem item,string text)
        {
            string icon=item.Kind=="settings-tab"||item.Kind=="setting"?"settings":"chat",kind=item.Kind;
            switch(item.LocalChinese){
                case "新建对话":icon="plus";break;
                case "搜索":icon="search";break;
                case "项目":icon="folder";break;
                case "创建项目":icon="plus";kind="auxiliary";break;
                case "作品":icon="artifacts";break;
                case "从模板新建":icon="template";kind="auxiliary";break;
                case "自定义":icon="sliders";break;
                case "最近会话":icon="chevron";break;
                case "筛选并分组最近会话":icon="filter";kind="auxiliary";break;
                case "显示全部":icon="chevron";break;
                case "设置":icon="settings";break;
                case "语言":icon="language";break;
                case "帮助":icon="help";break;
                case "升级套餐":icon="upgrade";break;
                case "获取应用和扩展":icon="devices";break;
                case "了解更多":icon="info";break;
                case "退出登录":icon="logout";break;
            }
            return new NavigationDisplayRow{Id=item.Id,Text=text,Icon=icon,Kind=kind,Bounds=item.Bounds};
        }
        void Notify(){var handler=Updated;if(!disposed&&handler!=null)handler();}
        public void Dispose(){if(disposed)return;disposed=true;Cancel();Current=null;cache.Clear();cacheOrder.Clear();retryAfter.Clear();}
    }
}
