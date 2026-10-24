using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using Condition = System.Windows.Automation.Condition;

namespace Yuqiao
{
    public sealed class ClaudeChromeSnapshot
    {
        public bool Connected, SidebarVisible, MenuVisible, TargetForeground, SettingsVisible;
        public bool MenuHasUnsupportedItems;
        public string Status = "", WindowKey = "";
        // All rectangles are physical desktop coordinates, not WPF DIPs.
        public Rect WindowBounds = Rect.Empty, SidebarBounds = Rect.Empty, MenuBounds = Rect.Empty, WorkArea = Rect.Empty, SettingsBounds = Rect.Empty;
        public Rect SettingsContentBounds = Rect.Empty;
        public string SettingsPageKey = "", SettingsPageChinese = "";
        public List<ClaudeChromeItem> Sidebar = new List<ClaudeChromeItem>();
        public List<ClaudeChromeItem> AccountMenu = new List<ClaudeChromeItem>();
        public List<ClaudeChromeItem> Settings = new List<ClaudeChromeItem>();
        public long ReadMs;
    }

    public sealed class ClaudeChromeItem
    {
        public string Id = "", Original = "", LocalChinese = "", Kind = "";
        public bool NeedsTranslation;
        public Rect Bounds = Rect.Empty;
    }

    // Read-only chrome adapter. Never reads account-header names, email Text,
    // composer values or message-body text. No focus, invoke, keys or network.
    public static class ClaudeChromeReader
    {
        private const int MaximumTitles = 20, MaximumTitleCharacters = 160;
        private const string OptionsSuffix = "のその他のオプション";
        private static readonly Dictionary<string, string> Navigation = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"ナビゲーション", "导航"}, {"新規作成", "新建对话"}, {"検索", "搜索"},
            {"プロジェクト", "项目"}, {"プロジェクトを作成", "创建项目"}, {"Artifacts", "作品"},
            {"テンプレートから新規作成", "从模板新建"}, {"カスタマイズ", "自定义"},
            {"最近の項目", "最近会话"}, {"最近の項目を絞り込んでグループ化", "筛选并分组最近会话"},
            {"すべて表示", "显示全部"}, {"アプリと拡張機能を入手", "获取应用和扩展"},
            {"Navigation", "导航"}, {"New chat", "新建对话"}, {"Search", "搜索"},
            {"Projects", "项目"}, {"Create project", "创建项目"}, {"Create from template", "从模板新建"},
            {"Customize", "自定义"}, {"Recents", "最近会话"}, {"Show all", "显示全部"},
            {"Get apps and extensions", "获取应用和扩展"}
        };
        private static readonly Dictionary<string, string> MenuLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"設定", "设置"}, {"言語", "语言"}, {"ヘルプ", "帮助"}, {"プランをアップグレード", "升级套餐"},
            {"アプリと拡張機能を入手", "获取应用和扩展"}, {"詳細情報", "了解更多"}, {"ログアウト", "退出登录"},
            {"Settings", "设置"}, {"Language", "语言"}, {"Help", "帮助"}, {"Upgrade plan", "升级套餐"},
            {"Get apps and extensions", "获取应用和扩展"}, {"Learn more", "了解更多"}, {"Log out", "退出登录"}
        };
        private static readonly Regex EmailLike = new Regex(@"[^\s＠@]+[＠@][^\s＠@]+", RegexOptions.Compiled);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);

        public static ClaudeChromeSnapshot Read() { return Read(IntPtr.Zero); }

        public static ClaudeChromeSnapshot Read(IntPtr expectedWindow)
        {
            ClaudeChromeSnapshot result = new ClaudeChromeSnapshot();
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                IntPtr window = FindUniqueWindow();
                if (expectedWindow != IntPtr.Zero && window != expectedWindow)
                    throw new InvalidOperationException("Claude 窗口已变化，等待重新连接页面辅助。");
                AutomationElement root = AutomationElement.FromHandle(window);
                AutomationElement document = FindDocument(root);
                result.Connected = true;
                result.WindowKey = "claude|" + window.ToInt64();
                result.TargetForeground = IsTargetOrSelfForeground(window);
                NativeRect native;
                if (GetWindowRect(window, out native) && native.Right > native.Left && native.Bottom > native.Top)
                    result.WindowBounds = new Rect(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
                MonitorInfo monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                IntPtr monitorHandle = MonitorFromWindow(window, 2);
                if (monitorHandle != IntPtr.Zero && GetMonitorInfo(monitorHandle, ref monitor)
                    && monitor.Work.Right > monitor.Work.Left && monitor.Work.Bottom > monitor.Work.Top)
                    result.WorkArea = new Rect(monitor.Work.Left, monitor.Work.Top, monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
                if (IsIconic(window) || !IsWindowVisible(window))
                { result.Status = "Claude 已最小化，页面辅助暂时隐藏。"; return result; }
                if (ClaudeSettingsReader.TryRead(document, result)) return result;
                AutomationElement sidebar = FindSidebar(document);
                if (sidebar == null) { result.Status = "当前未检测到展开的 Claude 侧栏。"; return result; }
                result.SidebarVisible = true;
                result.SidebarBounds = sidebar.Current.BoundingRectangle;
                ReadNavigation(sidebar, result.Sidebar);
                ReadRecentTitles(sidebar, result.Sidebar);
                ReadAccountMenu(sidebar, result);
                result.Sidebar.Sort(CompareItems);
                result.Status = result.MenuVisible ? "已同步 Claude 目录和账户菜单；账户信息已排除。" : "已同步 Claude 当前可见目录。";
            }
            catch (InvalidOperationException ex) { Clear(result); result.Status = ex.Message; }
            catch (ElementNotAvailableException) { Clear(result); result.Status = "Claude 页面正在切换，稍后重试。"; }
            catch (COMException) { Clear(result); result.Status = "Claude 暂未提供可读取的目录控件。"; }
            catch (UnauthorizedAccessException) { Clear(result); result.Status = "Windows 拒绝读取 Claude 目录，请使用相同权限运行。"; }
            catch (Win32Exception) { Clear(result); result.Status = "暂时无法定位 Claude 窗口。"; }
            catch (ArgumentException) { Clear(result); result.Status = "Claude 目录位置暂时不可用。"; }
            finally { result.ReadMs = watch.ElapsedMilliseconds; }
            return result;
        }

        private static void Clear(ClaudeChromeSnapshot value)
        {
            value.Connected = value.SidebarVisible = value.MenuVisible = value.TargetForeground = value.SettingsVisible = false;
            value.MenuHasUnsupportedItems = false;
            value.WindowKey = ""; value.WindowBounds = value.SidebarBounds = value.MenuBounds = value.WorkArea = value.SettingsBounds = Rect.Empty;
            value.Sidebar.Clear(); value.AccountMenu.Clear(); value.Settings.Clear();
        }
        private static IntPtr FindUniqueWindow()
        {
            HashSet<IntPtr> windows = new HashSet<IntPtr>();
            foreach (Process process in Process.GetProcessesByName("claude"))
            {
                try
                {
                    IntPtr hwnd = process.MainWindowHandle;
                    if (hwnd != IntPtr.Zero && IsWindow(hwnd) && IsWindowVisible(hwnd)) windows.Add(hwnd);
                }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            if (windows.Count != 1) throw new InvalidOperationException(windows.Count == 0 ? "请先打开 Claude 客户端。" : "发现多个 Claude 窗口，请保留一个后重试。");
            foreach (IntPtr value in windows) return value;
            return IntPtr.Zero;
        }
        private static AutomationElement FindDocument(AutomationElement root)
        {
            AutomationElement selected = null; int count = 0;
            foreach (AutomationElement document in root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)))
            {
                object supported; Uri url;
                if (!document.TryGetCurrentPattern(ValuePattern.Pattern, out supported)
                    || !Uri.TryCreate(((ValuePattern)supported).Current.Value, UriKind.Absolute, out url)
                    || url.Scheme != "https" || !String.Equals(url.Host, "claude.ai", StringComparison.OrdinalIgnoreCase)) continue;
                selected = document; count++;
            }
            if (count != 1) throw new InvalidOperationException("未找到唯一的 Claude 页面，暂不读取目录。");
            return selected;
        }
        private static AutomationElement FindSidebar(AutomationElement document)
        {
            AutomationElement selected = null; int count = 0;
            Condition sidebarNames = Names(new[] { "サイドバー", "Sidebar", "补充 サイドバー" });
            foreach (AutomationElement candidate in document.FindAll(TreeScope.Descendants, sidebarNames))
            {
                if (!Visible(candidate)) continue;
                bool navigation = candidate.FindFirst(TreeScope.Descendants, Names(new[] { "ナビゲーション", "Navigation" })) != null;
                bool newAndProjects = candidate.FindFirst(TreeScope.Descendants, Names(new[] { "新規作成", "New chat" })) != null
                    && candidate.FindFirst(TreeScope.Descendants, Names(new[] { "プロジェクト", "Projects" })) != null;
                if (!navigation && !newAndProjects) continue;
                selected = candidate; count++;
            }
            return count == 1 ? selected : null;
        }
        private static void ReadNavigation(AutomationElement sidebar, List<ClaudeChromeItem> items)
        {
            Condition type = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header));
            List<string> labels = new List<string>(Navigation.Keys);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AutomationElement node in sidebar.FindAll(TreeScope.Descendants, new AndCondition(type, Names(labels.ToArray()))))
            {
                if (!Visible(node) || InsideMenu(node, sidebar)) continue;
                string original = node.Current.Name;
                string translated; if (!Navigation.TryGetValue(original, out translated)) continue;
                Rect bounds = node.Current.BoundingRectangle;
                // Chromium exposes some heading/button labels twice (the recent
                // section is both a heading and an inner collapse button).
                if (!seen.Add(original)) continue;
                items.Add(new ClaudeChromeItem { Id = Runtime(node), Original = original, LocalChinese = translated,
                    Kind = original == "ナビゲーション" || original == "Navigation" || original == "最近の項目" || original == "Recents" ? "section" : "command", Bounds = bounds });
            }
        }
        private static void ReadRecentTitles(AutomationElement sidebar, List<ClaudeChromeItem> items)
        {
            Rect recentHeading = FirstVisibleBounds(sidebar, new[] { "最近の項目", "Recents" });
            if (recentHeading.IsEmpty) return;
            Rect showAll = FirstVisibleBounds(sidebar, new[] { "すべて表示", "Show all" });
            double bottom = showAll.IsEmpty ? sidebar.Current.BoundingRectangle.Bottom : showAll.Top;
            List<ClaudeChromeItem> titles = new List<ClaudeChromeItem>();
            foreach (AutomationElement group in sidebar.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group)))
            {
                if (titles.Count >= 100 || !Visible(group) || InsideMenu(group, sidebar)) continue;
                AutomationElement first = null, second = null; int buttons = 0;
                foreach (AutomationElement child in group.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                { if (buttons == 0) first = child; else if (buttons == 1) second = child; buttons++; }
                if (buttons != 2 || !Visible(first) || !Visible(second)) continue;
                Rect bounds = first.Current.BoundingRectangle;
                if (bounds.Top < recentHeading.Bottom - 2 || bounds.Bottom > bottom + 2) continue;
                // Only paired row controls can authorize reading the title name.
                // A lone avatar/account button is never read by this code path.
                string optionName = second.Current.Name ?? "";
                if (!optionName.EndsWith(OptionsSuffix, StringComparison.Ordinal)) continue;
                string title = first.Current.Name ?? "";
                if (optionName != title + OptionsSuffix || !SafeTitle(title)) continue;
                titles.Add(new ClaudeChromeItem { Id = Runtime(first), Original = title, Kind = "recent-title",
                    LocalChinese = "", NeedsTranslation = true, Bounds = bounds });
            }
            titles.Sort(CompareItems);
            HashSet<string> identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (ClaudeChromeItem item in titles)
            { if (identifiers.Add(item.Id)) items.Add(item); if (identifiers.Count >= MaximumTitles) break; }
        }
        private static void ReadAccountMenu(AutomationElement sidebar, ClaudeChromeSnapshot result)
        {
            AutomationElement selected = null; List<ClaudeChromeItem> selectedItems = null;
            bool selectedUnknown = false; int menus = 0;
            foreach (AutomationElement menu in sidebar.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu)))
            {
                if (!Visible(menu)) continue;
                List<ClaudeChromeItem> rows = new List<ClaudeChromeItem>(); bool unknown = false;
                foreach (AutomationElement row in menu.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))
                {
                    if (!Visible(row)) continue;
                    string name = row.Current.Name ?? "", translated;
                    // Only known menu option labels are returned. Menu.Name and
                    // all header Text nodes (account/email) are never accessed.
                    if (!MenuLabels.TryGetValue(name, out translated)) { unknown = true; continue; }
                    rows.Add(new ClaudeChromeItem { Id = Runtime(row), Original = name, LocalChinese = translated, Kind = "command", Bounds = row.Current.BoundingRectangle });
                }
                if (rows.Count < 2) continue;
                selected = menu; selectedItems = rows; selectedUnknown = unknown; menus++;
            }
            if (menus != 1) return;
            result.MenuVisible = true; result.MenuBounds = selected.Current.BoundingRectangle;
            result.MenuHasUnsupportedItems = selectedUnknown;
            selectedItems.Sort(CompareItems); result.AccountMenu.AddRange(selectedItems);
        }
        private static Rect FirstVisibleBounds(AutomationElement root, string[] names)
        {
            Rect result = Rect.Empty;
            foreach (AutomationElement node in root.FindAll(TreeScope.Descendants, Names(names)))
            {
                if (!Visible(node) || InsideMenu(node, root)) continue;
                Rect value = node.Current.BoundingRectangle;
                if (result.IsEmpty || value.Top < result.Top) result = value;
            }
            return result;
        }
        private static bool SafeTitle(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > MaximumTitleCharacters || EmailLike.IsMatch(value)) return false;
            foreach (char character in value) if (Char.IsControl(character)) return false;
            return true;
        }
        private static bool Visible(AutomationElement node)
        {
            if (node == null || node.Current.IsOffscreen) return false;
            Rect bounds = node.Current.BoundingRectangle;
            return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0;
        }
        private static bool InsideMenu(AutomationElement node, AutomationElement boundary)
        {
            AutomationElement current = TreeWalker.RawViewWalker.GetParent(node);
            for (int depth = 0; current != null && depth < 24; depth++, current = TreeWalker.RawViewWalker.GetParent(current))
            {
                if (Automation.Compare(current, boundary)) return false;
                if (current.Current.ControlType == ControlType.Menu) return true;
            }
            return false;
        }
        private static Condition Names(string[] values)
        {
            Condition[] conditions = new Condition[values.Length];
            for (int i = 0; i < values.Length; i++) conditions[i] = new PropertyCondition(AutomationElement.NameProperty, values[i]);
            return values.Length == 1 ? conditions[0] : new OrCondition(conditions);
        }
        private static string Runtime(AutomationElement node) { return String.Join(".", Array.ConvertAll(node.GetRuntimeId(), value => value.ToString())); }
        private static int CompareItems(ClaudeChromeItem first, ClaudeChromeItem second)
        { int vertical = first.Bounds.Top.CompareTo(second.Bounds.Top); return vertical != 0 ? vertical : first.Bounds.Left.CompareTo(second.Bounds.Left); }
        private static bool IsTargetOrSelfForeground(IntPtr target)
        {
            IntPtr foreground = GetForegroundWindow(); if (foreground == target) return true;
            uint pid; GetWindowThreadProcessId(foreground, out pid);
            using (Process self = Process.GetCurrentProcess()) if (pid == self.Id) return true;
            try { using (Process other = Process.GetProcessById((int)pid)) return String.Equals(other.ProcessName, "claude", StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }
}
