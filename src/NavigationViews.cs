using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.RegularExpressions;
using System.Text;
using System.Globalization;

namespace Yuqiao
{
    // These views receive display-only Chinese strings, never source labels.
    public sealed class NavigationContent : StackPanel
    {
        public StackPanel Sidebar, AccountMenu;
        public TextBlock Status;
        public Expander MenuSection;
        public Expander SettingsTabs;
        private readonly StackPanel settingsTabRows;
        private bool menuWasOpen;
        private bool settingsMode;
        private readonly TextBlock heading;
        private string sidebarSignature = "", menuSignature = "";
        private const string ClosedHint = "点击左下角头像后显示菜单。";

        public NavigationContent()
        {
            heading = new TextBlock { Text = "目录", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = FloatingUi.Accent, Margin = new Thickness(8, 0, 0, 8) };
            Children.Add(heading);
            settingsTabRows = new StackPanel();
            SettingsTabs = new Expander { Header = "设置栏目", FontSize = 12, IsExpanded = false, Visibility = Visibility.Collapsed, Content = settingsTabRows, Margin = new Thickness(8, 0, 4, 6) };
            Children.Add(SettingsTabs);
            Sidebar = new StackPanel();
            AutomationProperties.SetName(Sidebar, "中文目录");
            Children.Add(Sidebar);
            AccountMenu = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetName(AccountMenu, "中文头像菜单");
            MenuSection = new Expander { Header = "头像菜单", FontSize = 12, Margin = new Thickness(0, 8, 0, 0), IsExpanded = false, Content = AccountMenu };
            Children.Add(MenuSection);
            Status = FloatingUi.StatusText(ClosedHint);
            Children.Add(Status);
        }

        public void Update(List<NavigationDisplayRow> sidebar, List<NavigationDisplayRow> accountMenu, string status, bool menuOpen)
        {
            if (settingsMode)
            {
                settingsMode = false; heading.Text = "目录"; MenuSection.Visibility = Visibility.Visible;
                SettingsTabs.Visibility = Visibility.Collapsed; settingsTabRows.Children.Clear();
                sidebarSignature = ""; menuSignature = "";
            }
            string nextSidebar = NavigationRows.Signature(sidebar);
            string nextMenu = menuOpen ? NavigationRows.Signature(accountMenu) : "closed";
            if (nextSidebar != sidebarSignature)
            {
                Sidebar.Children.Clear();
                foreach (NavigationRows.Group group in NavigationRows.GroupRows(sidebar)) Sidebar.Children.Add(NavigationRows.MakeRow(group, 32));
                sidebarSignature = nextSidebar;
            }
            if (nextMenu != menuSignature)
            {
                AccountMenu.Children.Clear();
                if (menuOpen) foreach (NavigationRows.Group group in NavigationRows.GroupRows(accountMenu)) AccountMenu.Children.Add(NavigationRows.MakeRow(group, 32));
                menuSignature = nextMenu;
            }
            if (menuOpen != menuWasOpen) MenuSection.IsExpanded = menuOpen;
            menuWasOpen = menuOpen;
            string message = NavigationRows.Clean(status, "");
            if (!menuOpen && message.IndexOf(ClosedHint, StringComparison.Ordinal) < 0)
                message = message.Length == 0 ? ClosedHint : message + Environment.NewLine + ClosedHint;
            if (Status.Text != message) Status.Text = message;
        }

        public void UpdateSettings(List<NavigationDisplayRow> rows, string status)
        {
            if (!settingsMode)
            {
                settingsMode = true; heading.Text = "设置中文";
                MenuSection.Visibility = Visibility.Collapsed; MenuSection.IsExpanded = false;
                AccountMenu.Children.Clear(); menuWasOpen = false;
                sidebarSignature = ""; menuSignature = "";
            }
            string next = "settings:" + NavigationRows.Signature(rows, false);
            if (next != sidebarSignature)
            {
                Sidebar.Children.Clear(); settingsTabRows.Children.Clear();
                if (rows != null) foreach (NavigationDisplayRow row in rows)
                {
                    if (row == null) continue;
                    if (row.Kind == "section" && (row.Id == "settings-tabs" || row.Text == "设置栏目")) continue;
                    if (row.Kind == "settings-tab") settingsTabRows.Children.Add(NavigationRows.MakeSettingRow(row));
                    else Sidebar.Children.Add(NavigationRows.MakeSettingRow(row));
                }
                SettingsTabs.Visibility = settingsTabRows.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                sidebarSignature = next;
            }
            string message = NavigationRows.Clean(status, "等待设置内容…");
            if (Status.Text != message) Status.Text = message;
        }
    }

    public sealed class FloatingNavigationWindow : Window
    {
        // Retained for callers needing a compact list, used automatically when
        // the available height cannot preserve source row positions.
        public NavigationContent View;
        public CheckBox Follow;
        public Button FullMode;
        private bool allowClose;
        private readonly Canvas aligned = new Canvas { Background = FloatingUi.Surface };
        private readonly ScrollViewer scroller;
        private readonly Grid header;
        private readonly TextBlock caption;
        private List<NavigationDisplayRow> latestRows, latestMenu;
        private Rect latestSidebarBounds, latestMenuBounds;
        private string latestStatus;
        private bool latestMenuOpen, updating, settingsMode;
        private double latestScale = 1;
        private string renderedSignature = "";
        private const double ContentTop = 33; // 4 outer margin + 1 border + 28 header.
        private readonly Canvas settingsCanvas = new Canvas { Background = FloatingUi.Surface, ClipToBounds = true };
        private readonly StackPanel settingsList = new StackPanel();
        private readonly Grid settingsHost = new Grid();
        private readonly ScrollViewer settingsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        private readonly Button settingsBodyButton = new Button { Content = "当前设置", FontSize = 11, Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(4, 1, 2, 1) };
        private readonly Button settingsTabsButton = new Button { Content = "左侧栏目", FontSize = 11, Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(2, 1, 4, 1) };
        private List<NavigationDisplayRow> settingsRows = new List<NavigationDisplayRow>();
        private Rect settingsBounds = Rect.Empty, settingsContentBounds = Rect.Empty;
        private double settingsScale = 1;
        private bool settingsShowTabs, settingsAligned;
        private string settingsSignature = "", settingsStatus = "", settingsPage = "";

        public FloatingNavigationWindow()
        {
            FloatingUi.Configure(this, "语桥 · 中文菜单", 190, 700, 160, 220);
            Follow = FloatingUi.FollowControl();
            Follow.Content = "跟随"; Follow.FontSize = 11; Follow.ToolTip = "保持每一行与原菜单对齐；拖动标题栏后可手动放置。";
            FullMode = FloatingUi.SmallButton("隐藏", "只隐藏目录窗，可从顶端开关恢复");
            FullMode.FontSize = 11; FullMode.Padding = new Thickness(5, 1, 5, 1); FullMode.Margin = new Thickness(0, 3, 5, 3);
            Grid root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); root.RowDefinitions.Add(new RowDefinition());
            header = new Grid { Background = FloatingUi.Header };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            caption = new TextBlock { Text = "中文菜单", FontSize = 11, Foreground = FloatingUi.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 3, 0), IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
            header.Children.Add(caption);
            Follow.Margin = new Thickness(0, 0, 8, 0); Follow.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(Follow, 1); header.Children.Add(Follow); Grid.SetColumn(FullMode, 2); header.Children.Add(FullMode); root.Children.Add(header);
            View = new NavigationContent();
            scroller = new ScrollViewer { Content = aligned, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = FloatingUi.Surface };
            Grid.SetRow(scroller, 1); root.Children.Add(scroller);
            Content = new Border { Margin = new Thickness(4), BorderThickness = new Thickness(1), BorderBrush = FloatingUi.Line, Background = FloatingUi.Surface, CornerRadius = new CornerRadius(6), Child = root };
            settingsHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            settingsHost.RowDefinitions.Add(new RowDefinition());
            StackPanel choices = new StackPanel { Orientation = Orientation.Horizontal, Background = FloatingUi.Surface };
            choices.Children.Add(settingsBodyButton); choices.Children.Add(settingsTabsButton); settingsHost.Children.Add(choices);
            Grid.SetRow(settingsScroll, 1); settingsHost.Children.Add(settingsScroll);
            settingsBodyButton.Click += delegate { settingsShowTabs = false; settingsSignature = ""; RenderSettings(); };
            settingsTabsButton.Click += delegate { settingsShowTabs = true; settingsSignature = ""; RenderSettings(); };
            header.MouseLeftButtonDown += DragHeader;
            SizeChanged += delegate { if (settingsMode && settingsAligned) RenderSettings(); else if (latestRows != null && !updating) RenderAligned(); };
            LocationChanged += delegate { if (settingsMode && settingsAligned) RenderSettings(); else if (latestRows != null && !updating) RenderAligned(); };
            Follow.Checked += delegate { if (settingsMode && settingsAligned) RenderSettings(); else if (!settingsMode && latestRows != null) RenderAligned(); };
            Follow.Unchecked += delegate { if (settingsMode && settingsAligned) RenderSettings(); else if (!settingsMode && latestRows != null) RenderAligned(); };
        }

        public void UpdateAligned(List<NavigationDisplayRow> rows, List<NavigationDisplayRow> menu, string status, bool menuOpen, Rect sidebarBoundsDip, Rect menuBoundsDip, double scale)
        {
            if (settingsMode)
            {
                settingsMode = false; settingsAligned = false; Title = "语桥 · 中文菜单"; caption.Text = "中文菜单";
                scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                renderedSignature = "";
                // Clear the compact view as well; it may be used after a drag.
                View.Update(rows, menu, status, menuOpen);
                scroller.ScrollToVerticalOffset(0);
            }
            latestRows = rows ?? new List<NavigationDisplayRow>(); latestMenu = menu ?? new List<NavigationDisplayRow>();
            latestStatus = status; latestMenuOpen = menuOpen; latestSidebarBounds = sidebarBoundsDip; latestMenuBounds = menuBoundsDip;
            latestScale = !Double.IsNaN(scale) && !Double.IsInfinity(scale) && scale > 0 ? scale : 1;
            RenderAligned();
        }

        public void UpdateSettings(List<NavigationDisplayRow> rows, string status)
        {
            bool entering = !settingsMode;
            settingsAligned = false;
            settingsMode = true; Title = "语桥 · 设置中文"; caption.Text = "设置中文";
            View.UpdateSettings(rows, status); header.ToolTip = NavigationRows.Clean(status, "设置内容的中文说明。");
            if (entering) { aligned.Children.Clear(); renderedSignature = ""; scroller.Content = View; scroller.ScrollToVerticalOffset(0); }
        }

        public void UpdateSettingsAligned(List<NavigationDisplayRow> rows, string status, Rect dialogBoundsDip, Rect contentBoundsDip, double scale, string pageChinese)
        {
            bool entering = !settingsMode || !settingsAligned;
            settingsMode = settingsAligned = true;
            settingsRows = rows ?? new List<NavigationDisplayRow>(); settingsStatus = status ?? "";
            settingsBounds = dialogBoundsDip; settingsContentBounds = contentBoundsDip;
            settingsScale = !Double.IsNaN(scale) && !Double.IsInfinity(scale) && scale > 0 ? scale : 1;
            settingsPage = NavigationRows.Clean(pageChinese, "设置");
            Title = "语桥 · 设置中文"; caption.Text = settingsPage + " · 中文";
            if (entering) { settingsShowTabs = false; settingsSignature = ""; aligned.Children.Clear(); }
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.Content = settingsHost;
            RenderSettings();
        }

        private void RenderSettings()
        {
            if (!settingsMode || !settingsAligned) return;
            double top = Double.IsNaN(Top) ? 0 : Top;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            double width = Math.Max(100, (ActualWidth > 0 ? ActualWidth : Width) - 10);
            double origin = top + ContentTop + 26, viewport = Math.Max(1, height - ContentTop - 31);
            bool outsideSide = !settingsBounds.IsEmpty && (Left + Width <= settingsBounds.Left || Left >= settingsBounds.Right);
            bool align = Follow.IsChecked == true && outsideSide && top <= settingsBounds.Top + 8 && top + height >= settingsBounds.Bottom - 8;
            string signature = NavigationRows.Signature(settingsRows) + "|" + settingsShowTabs + "|" + align + "|" + top + "|" + height + "|" + width + "|" + settingsScale + "|" + settingsContentBounds;
            settingsBodyButton.FontWeight = settingsShowTabs ? FontWeights.Normal : FontWeights.SemiBold;
            settingsTabsButton.FontWeight = settingsShowTabs ? FontWeights.SemiBold : FontWeights.Normal;
            header.ToolTip = (align ? "中文与原设置逐行同高度。" : "空间有限，当前为逐项阅读模式。") + "\n" + settingsStatus;
            if (signature == settingsSignature) return;
            settingsSignature = signature;
            settingsCanvas.Children.Clear(); settingsList.Children.Clear();
            List<NavigationDisplayRow> selected = new List<NavigationDisplayRow>();
            foreach (NavigationDisplayRow row in settingsRows)
                if (row != null && !row.Bounds.IsEmpty && (row.Kind == "settings-tab") == settingsShowTabs) selected.Add(row);
            selected.Sort(delegate(NavigationDisplayRow a, NavigationDisplayRow b) { return a.Bounds.Top.CompareTo(b.Bounds.Top); });
            if (selected.Count == 0)
            {
                settingsList.Children.Add(new TextBlock { Text = settingsShowTabs ? "当前未显示设置栏目。" : "本页正文暂未适配。可切换“左侧栏目”查看栏目中文。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
                settingsScroll.Content = settingsList; return;
            }
            if (!align)
            {
                foreach (NavigationDisplayRow row in selected) settingsList.Children.Add(NavigationRows.MakeSettingRow(row));
                settingsScroll.Content = settingsList; return;
            }
            settingsCanvas.Width = width; settingsCanvas.Height = viewport; settingsScroll.Content = settingsCanvas;
            for (int i = 0; i < selected.Count; i++)
            {
                NavigationDisplayRow row = selected[i]; double sourceTop = row.Bounds.Top / settingsScale;
                double y = sourceTop - origin;
                if (y < 0 || y >= viewport) continue;
                double nextTop = i + 1 < selected.Count ? selected[i + 1].Bounds.Top / settingsScale : top + height - 5;
                double available = Math.Min(viewport - y, Math.Max(16, nextTop - sourceTop - 3));
                Border item = NavigationRows.MakeAlignedSettingRow(row, width, available);
                Canvas.SetLeft(item, 0); Canvas.SetTop(item, y); settingsCanvas.Children.Add(item);
            }
        }

        private void RenderAligned()
        {
            if (updating) return; updating = true;
            try
            {
                double top = Double.IsNaN(Top) ? 0 : Top;
                double height = ActualHeight > 0 ? ActualHeight : Height;
                double width = Math.Max(100, (ActualWidth > 0 ? ActualWidth : Width) - 10);
                double viewport = Math.Max(1, height - ContentTop - 5);
                bool compact = Follow.IsChecked != true || (!latestSidebarBounds.IsEmpty && latestSidebarBounds.Height > height + 16);
                string status = NavigationRows.Clean(latestStatus, "");
                string nextSignature = NavigationRows.Signature(latestRows) + NavigationRows.Signature(latestMenu)
                    + "|" + latestMenuOpen + "|" + latestScale.ToString("R", CultureInfo.InvariantCulture)
                    + "|" + latestSidebarBounds.ToString(CultureInfo.InvariantCulture) + "|" + latestMenuBounds.ToString(CultureInfo.InvariantCulture)
                    + "|" + top.ToString("R", CultureInfo.InvariantCulture) + "|" + height.ToString("R", CultureInfo.InvariantCulture)
                    + "|" + width.ToString("R", CultureInfo.InvariantCulture) + "|" + compact;
                header.ToolTip = status + (compact ? "\n当前为紧凑列表，行位置未与原菜单对齐。" : "");
                if (compact) View.Update(latestRows, latestMenu, status + "\n当前为可滚动列表。", latestMenuOpen);
                if (nextSignature == renderedSignature) return;
                renderedSignature = nextSignature;
                aligned.Children.Clear();
                if (compact)
                {
                    scroller.Content = View;
                    return;
                }
                scroller.Content = aligned;
                scroller.ScrollToVerticalOffset(0);
                aligned.Width = width; aligned.Height = viewport;
                foreach (NavigationRows.Group group in NavigationRows.GroupRows(latestRows))
                {
                    if (group.Row.Bounds.IsEmpty) continue;
                    double rowHeight = Math.Max(18, Math.Min(34, group.Row.Bounds.Height / latestScale));
                    double y = NavigationRows.AlignedTop(group.Row.Bounds, latestScale, top, ContentTop, rowHeight);
                    // The opaque menu card owns this vertical band; suppress its
                    // underlying directory rows as well to prevent doubled text.
                    double center = y + rowHeight / 2 + top + ContentTop;
                    if (latestMenuOpen && !latestMenuBounds.IsEmpty && center >= latestMenuBounds.Top && center <= latestMenuBounds.Bottom) continue;
                    Border row = NavigationRows.MakeRow(group, rowHeight); row.Width = width;
                    Canvas.SetTop(row, y); aligned.Children.Add(row);
                    aligned.Height = Math.Max(aligned.Height, y + rowHeight + 4);
                }
                if (latestMenuOpen && !latestMenuBounds.IsEmpty)
                {
                    double cardTop = latestMenuBounds.Top - top - ContentTop;
                    Canvas menuCanvas = new Canvas { Background = FloatingUi.Surface };
                    Border card = new Border { Background = FloatingUi.Surface, BorderBrush = FloatingUi.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Width = width, Height = Math.Max(24, latestMenuBounds.Height), Child = menuCanvas, Tag = "avatar-menu-card" };
                    Canvas.SetTop(card, cardTop); Panel.SetZIndex(card, 10); aligned.Children.Add(card);
                    foreach (NavigationRows.Group group in NavigationRows.GroupRows(latestMenu))
                    {
                        if (group.Row.Bounds.IsEmpty) continue;
                        double rowHeight = Math.Max(18, Math.Min(32, group.Row.Bounds.Height / latestScale));
                        Border row = NavigationRows.MakeRow(group, rowHeight); row.Width = Math.Max(1, width - 2);
                        double y = NavigationRows.AlignedTop(group.Row.Bounds, latestScale, latestMenuBounds.Top, 1, rowHeight);
                        Canvas.SetTop(row, y); menuCanvas.Children.Add(row);
                    }
                    aligned.Height = Math.Max(aligned.Height, cardTop + card.Height + 3);
                }
            }
            finally { updating = false; }
        }

        private void DragHeader(object sender, MouseButtonEventArgs e)
        {
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && node != header)
            { if (node is ButtonBase) return; node = node is Visual ? VisualTreeHelper.GetParent(node) : null; }
            if (e.ClickCount != 1) return;
            bool followed = Follow.IsChecked == true; Point before = new Point(Left, Top); Follow.IsChecked = false;
            try { DragMove(); } catch (InvalidOperationException) { }
            if (Math.Abs(Left - before.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(Top - before.Y) < SystemParameters.MinimumVerticalDragDistance) Follow.IsChecked = followed;
            e.Handled = true;
        }

        public void ClosePermanently() { allowClose = true; Close(); }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!allowClose) { e.Cancel = true; FullMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, FullMode)); }
        }
    }

    internal static class NavigationRows
    {
        internal sealed class Group { internal NavigationDisplayRow Row; internal readonly List<NavigationDisplayRow> Auxiliary = new List<NavigationDisplayRow>(); }
        internal static string Signature(List<NavigationDisplayRow> rows, bool includeBounds = true)
        {
            StringBuilder result = new StringBuilder(); result.Append(rows == null ? 0 : rows.Count).Append(':');
            if (rows != null) foreach (NavigationDisplayRow row in rows)
            {
                if (row == null) { result.Append("null;"); continue; }
                foreach (string value in new[] { row.Id, row.Text, row.Icon, row.Kind, includeBounds ? row.Bounds.ToString(CultureInfo.InvariantCulture) : "" })
                { string text = value ?? ""; result.Append(text.Length).Append(':').Append(text); }
            }
            return result.ToString();
        }
        internal static string Clean(string value, string fallback)
        {
            if (String.IsNullOrWhiteSpace(value)) return fallback;
            if (Regex.IsMatch(value, @"[\u3040-\u30ff\uff66-\uff9f]|[→←↔⇒⇨]")) return "暂未译出";
            return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
        internal static double AlignedTop(Rect physical, double scale, double windowTop, double contentTop, double rowHeight)
        { return (physical.Top + physical.Height / 2) / scale - windowTop - contentTop - rowHeight / 2; }
        private static bool Auxiliary(NavigationDisplayRow row)
        { return row.Kind == "auxiliary" || row.Icon == "filter" || (row.Id ?? "").Contains("create-project") || (row.Id ?? "").Contains("template"); }
        internal static List<Group> GroupRows(List<NavigationDisplayRow> rows)
        {
            List<Group> result = new List<Group>();
            if (rows == null) return result;
            List<NavigationDisplayRow> auxiliary = new List<NavigationDisplayRow>();
            foreach (NavigationDisplayRow row in rows)
            {
                if (row == null || (row.Kind == "section" && row.Text == "导航")) continue;
                if (Auxiliary(row)) auxiliary.Add(row); else result.Add(new Group { Row = row });
            }
            result.Sort(delegate(Group a, Group b) { return a.Row.Bounds.IsEmpty || b.Row.Bounds.IsEmpty ? 0 : a.Row.Bounds.Top.CompareTo(b.Row.Bounds.Top); });
            foreach (NavigationDisplayRow row in auxiliary)
            {
                Group closest = null; double best = Double.PositiveInfinity;
                foreach (Group group in result)
                {
                    if (row.Bounds.IsEmpty || group.Row.Bounds.IsEmpty) continue;
                    double delta = Math.Abs(row.Bounds.Top + row.Bounds.Height / 2 - group.Row.Bounds.Top - group.Row.Bounds.Height / 2);
                    if (delta <= Math.Max(12, Math.Min(row.Bounds.Height, group.Row.Bounds.Height) / 2) && delta < best) { best = delta; closest = group; }
                }
                if (closest != null) closest.Auxiliary.Add(row);
            }
            return result;
        }
        internal static Border MakeRow(Group group, double height)
        {
            NavigationDisplayRow row = group.Row;
            string text = Clean(row.Text, row.Kind == "recent-title" ? "未命名会话" : "暂未适配");
            Grid line = new Grid { Margin = new Thickness(8, 0, 7, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool showIcon = row.Kind != "section" || !String.IsNullOrWhiteSpace(row.Icon);
            if (showIcon) line.Children.Add(Icon(row.Icon));
            TextBlock label = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text, Foreground = row.Kind == "section" ? FloatingUi.Muted : FloatingUi.Text, FontWeight = row.Kind == "section" ? FontWeights.SemiBold : FontWeights.Normal };
            Grid.SetColumn(label, showIcon ? 1 : 0); if (!showIcon) Grid.SetColumnSpan(label, 2); line.Children.Add(label);
            StackPanel tail = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (NavigationDisplayRow auxiliary in group.Auxiliary)
            {
                string hint = Clean(auxiliary.Text, "更多选项");
                Border action = new Border { Child = Icon(auxiliary.Icon), Width = 20, Height = 24, ToolTip = hint, Background = Brushes.Transparent };
                AutomationProperties.SetName(action, hint); tail.Children.Add(action);
            }
            if (row.Kind == "submenu" || row.Icon == "language" || row.Icon == "help") tail.Children.Add(Icon("chevron"));
            Grid.SetColumn(tail, 2); line.Children.Add(tail);
            Border container = new Border { Child = line, Height = height, Background = FloatingUi.Surface, ToolTip = text, Tag = row.Id ?? "" };
            AutomationProperties.SetName(container, text);
            return container;
        }
        internal static Border MakeSettingRow(NavigationDisplayRow row)
        {
            string text = Clean(row.Text, "暂未适配");
            bool description = row.Kind == "description";
            TextBlock label = new TextBlock { Text = text, FontSize = description ? 11 : 12, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, ToolTip = text, Foreground = description ? FloatingUi.Muted : FloatingUi.Text, LineHeight = description ? 16 : 18, FontWeight = row.Kind == "section" ? FontWeights.SemiBold : FontWeights.Normal };
            Border result = new Border { Child = label, Background = FloatingUi.Surface, Padding = new Thickness(description ? 12 : 8, 4, 8, 5), Tag = row.Id ?? "", ToolTip = text };
            AutomationProperties.SetName(result, text); return result;
        }
        internal static Border MakeAlignedSettingRow(NavigationDisplayRow row, double width, double available)
        {
            string text = Clean(row.Text, "暂未适配");
            bool description = row.Kind == "description", section = row.Kind == "section";
            string display = text;
            int colon = text.IndexOf('：');
            if (colon > 0 && available >= 35) display = text.Substring(0, colon + 1) + "\n" + text.Substring(colon + 1);
            TextBlock label = new TextBlock {
                Text = display, FontSize = description ? 11 : 12, LineHeight = 17,
                TextWrapping = available >= 34 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text,
                Foreground = section ? FloatingUi.Accent : description ? FloatingUi.Muted : FloatingUi.Text,
                FontWeight = section ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Top
            };
            Border result = new Border { Child = label, Width = width, MaxHeight = available, Padding = new Thickness(8,0,6,0), Background = FloatingUi.Surface, ClipToBounds = true, Tag = row.Id ?? "", ToolTip = text };
            AutomationProperties.SetName(result, text); return result;
        }
        internal static FrameworkElement Icon(string name)
        {
            string data;
            switch (name ?? "")
            {
                case "plus": data = "M12,5 L12,19 M5,12 L19,12"; break;
                case "search": data = "M10,3 A7,7 0 1 1 9.9,3 M15,15 L21,21"; break;
                case "folder": data = "M3,7 L3,19 L21,19 L21,7 L12,7 L10,4 L3,4 Z"; break;
                case "artifacts": data = "M12,2 L14,8 L20,6 L17,12 L22,16 L15,17 L12,23 L9,17 L2,16 L7,12 L4,6 L10,8 Z"; break;
                case "sliders": data = "M4,5 L20,5 M4,12 L20,12 M4,19 L20,19 M8,2 L8,8 M16,9 L16,15 M10,16 L10,22"; break;
                case "chevron": data = "M9,5 L16,12 L9,19"; break;
                case "settings": data = "M12,7 A5,5 0 1 1 11.9,7 M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12 M5,5 L7,7 M17,17 L19,19 M5,19 L7,17 M17,7 L19,5"; break;
                case "language": data = "M3,5 L15,5 M9,2 L9,5 M13,5 Q12,13 4,17 M5,8 Q8,14 13,16 M14,21 L18,11 L22,21 M16,17 L20,17"; break;
                case "help": data = "M12,2 A10,10 0 1 1 11.9,2 M8,9 Q8,5 12,5 Q17,6 14,10 L12,12 L12,14 M12,17 L12,18"; break;
                case "upgrade": data = "M12,20 L12,4 M5,11 L12,4 L19,11"; break;
                case "devices": data = "M2,4 L17,4 L17,16 L2,16 Z M7,20 L12,20 M9,16 L9,20 M19,9 L23,9 L23,21 L19,21 Z"; break;
                case "info": data = "M12,2 A10,10 0 1 1 11.9,2 M12,10 L12,18 M12,6 L12,7"; break;
                case "logout": data = "M10,3 L3,3 L3,21 L10,21 M8,12 L22,12 M17,7 L22,12 L17,17"; break;
                case "filter": data = "M3,5 L21,5 M6,12 L18,12 M9,19 L15,19"; break;
                case "template": data = "M3,3 L21,3 L21,21 L3,21 Z M3,9 L21,9 M10,9 L10,21"; break;
                default: data = "M3,4 L21,4 L21,17 L10,17 L5,21 L5,17 L3,17 Z"; break;
            }
            Path path = new Path { Data = Geometry.Parse(data), Stroke = FloatingUi.Muted, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            Canvas glyph = new Canvas { Width = 24, Height = 24 }; glyph.Children.Add(path);
            return new Viewbox { Width = 13, Height = 13, Stretch = Stretch.Uniform, Child = glyph, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        }
    }

    // Returns an entirely unobstructed settings sidecar, or Empty. Geometry uses
    // logical coordinates only and never resizes or moves the target application.
    public static class SettingsPlacement
    {
        private const double Padding = 8, EdgeTolerance = 0.01, PreferredWidth = 190, MinimumWidth = 160, MinimumHeight = 180, MaximumHeight = 610;

        public static Rect Find(Rect settingsBounds, Rect workArea)
        {
            if (!Valid(settingsBounds) || !Valid(workArea)) return Rect.Empty;
            Rect blocked = settingsBounds;
            blocked.Inflate(Padding, Padding);
            if (!Valid(blocked)) return Rect.Empty;

            Rect right = Region(Math.Max(workArea.Left, blocked.Right + EdgeTolerance), workArea.Top, workArea.Right, workArea.Bottom);
            Rect left = Region(workArea.Left, workArea.Top, Math.Min(workArea.Right, blocked.Left - EdgeTolerance), workArea.Bottom);
            foreach (double width in new[] { PreferredWidth, MinimumWidth })
            {
                Rect candidate = Side(right, width, settingsBounds.Top, settingsBounds.Height, false);
                if (Accept(candidate, blocked, workArea)) return candidate;
                candidate = Side(left, width, settingsBounds.Top, settingsBounds.Height, true);
                if (Accept(candidate, blocked, workArea)) return candidate;
            }

            Rect above = Region(workArea.Left, workArea.Top, workArea.Right, Math.Min(workArea.Bottom, blocked.Top - EdgeTolerance));
            Rect below = Region(workArea.Left, Math.Max(workArea.Top, blocked.Bottom + EdgeTolerance), workArea.Right, workArea.Bottom);
            Rect horizontal = Horizontal(above, settingsBounds.Left, true);
            if (Accept(horizontal, blocked, workArea)) return horizontal;
            horizontal = Horizontal(below, settingsBounds.Left, false);
            return Accept(horizontal, blocked, workArea) ? horizontal : Rect.Empty;
        }

        private static Rect Side(Rect region, double width, double preferredTop, double preferredHeight, bool left)
        {
            if (region.IsEmpty || region.Width < width || region.Height < MinimumHeight) return Rect.Empty;
            double height = Math.Min(Math.Max(MinimumHeight, preferredHeight), region.Height);
            double top = Math.Max(region.Top, Math.Min(preferredTop, region.Bottom - height));
            return new Rect(left ? region.Right - width : region.Left, top, width, height);
        }

        private static Rect Horizontal(Rect region, double preferredLeft, bool above)
        {
            if (region.IsEmpty || region.Width < MinimumWidth || region.Height < MinimumHeight) return Rect.Empty;
            double width = Math.Min(PreferredWidth, region.Width), height = Math.Min(MaximumHeight, region.Height);
            double left = Math.Max(region.Left, Math.Min(preferredLeft, region.Right - width));
            return new Rect(left, above ? region.Bottom - height : region.Top, width, height);
        }

        private static Rect Region(double left, double top, double right, double bottom)
        { return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : Rect.Empty; }
        private static bool Accept(Rect candidate, Rect blocked, Rect area)
        { return !candidate.IsEmpty && area.Contains(candidate) && !candidate.IntersectsWith(blocked); }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool Valid(Rect value)
        { return !value.IsEmpty && value.Width > 0 && value.Height > 0 && Finite(value.X) && Finite(value.Y) && Finite(value.Width) && Finite(value.Height) && Finite(value.Right) && Finite(value.Bottom); }
    }

    // Pure logical-coordinate geometry. No display inspection or window changes.
    // If the work area cannot contain the requested size, the origin stays inside
    // its bounds; the caller must reduce the size to keep the entire panel inside.
    public static class NavigationLayout
    {
        public static Point Position(Rect target, Rect sidebar, Size size, Rect workArea, Rect inputAvoid)
        {
            if (!Valid(workArea) || workArea.Width <= 0 || workArea.Height <= 0)
                throw new ArgumentException("工作区大小无效。", "workArea");
            if (!Valid(target)) throw new ArgumentException("目标窗口范围无效。", "target");
            if (size.IsEmpty || !Finite(size.Width) || !Finite(size.Height) || size.Width <= 0 || size.Height <= 0)
                throw new ArgumentException("目录窗口大小无效。", "size");

            bool hasSidebar = Valid(sidebar) && sidebar.Width > 0 && sidebar.Height > 0;
            bool hasInput = Valid(inputAvoid) && inputAvoid.Width > 0 && inputAvoid.Height > 0;
            double outsideLeft = target.Left - size.Width - 8;
            double preferredX = outsideLeft >= workArea.Left ? outsideLeft : (hasSidebar ? sidebar.Right + 8 : target.Right + 8);
            Point preferred = Clamp(new Point(preferredX, target.Top + 50), size, workArea);
            if (Overlap(preferred, size, sidebar, hasSidebar) == 0 && Overlap(preferred, size, inputAvoid, hasInput) == 0)
                return preferred;

            List<Point> candidates = new List<Point>();
            // Above the input comes first when it is needed and can fit. The
            // sidebar remains a higher priority: its controls must stay clickable.
            if (hasInput)
                candidates.Add(Clamp(new Point(preferred.X, inputAvoid.Top - size.Height - 8), size, workArea));
            candidates.Add(preferred);

            List<double> xs = new List<double> { preferred.X, outsideLeft, target.Right + 8, workArea.Left, workArea.Right - size.Width };
            List<double> ys = new List<double> { preferred.Y, workArea.Top, workArea.Bottom - size.Height };
            if (hasSidebar)
            {
                xs.Add(sidebar.Right + 8); xs.Add(sidebar.Left - size.Width - 8);
                xs.Add(sidebar.Right); xs.Add(sidebar.Left - size.Width);
                ys.Add(sidebar.Top - size.Height - 8); ys.Add(sidebar.Bottom + 8);
                ys.Add(sidebar.Top - size.Height); ys.Add(sidebar.Bottom);
            }
            if (hasInput)
            {
                xs.Add(inputAvoid.Left - size.Width - 8); xs.Add(inputAvoid.Right + 8);
                xs.Add(inputAvoid.Left - size.Width); xs.Add(inputAvoid.Right);
                ys.Add(inputAvoid.Top - size.Height - 8); ys.Add(inputAvoid.Bottom + 8);
                ys.Add(inputAvoid.Top - size.Height); ys.Add(inputAvoid.Bottom);
            }
            foreach (double x in xs) foreach (double y in ys) candidates.Add(Clamp(new Point(x, y), size, workArea));

            Point best = candidates[0];
            double bestSidebar = Double.PositiveInfinity, bestInput = Double.PositiveInfinity;
            foreach (Point candidate in candidates)
            {
                double sidebarOverlap = Overlap(candidate, size, sidebar, hasSidebar);
                double inputOverlap = Overlap(candidate, size, inputAvoid, hasInput);
                if (sidebarOverlap < bestSidebar - 0.000001
                    || (Math.Abs(sidebarOverlap - bestSidebar) < 0.000001 && inputOverlap < bestInput - 0.000001))
                {
                    best = candidate; bestSidebar = sidebarOverlap; bestInput = inputOverlap;
                }
                if (bestSidebar == 0 && bestInput == 0) return best;
            }
            // A crowded or unusually small work area may make overlap unavoidable.
            // Keep the bounded position with least sidebar, then input obstruction.
            return best;
        }

        private static Point Clamp(Point p, Size size, Rect area)
        {
            return new Point(Math.Max(area.Left, Math.Min(p.X, Math.Max(area.Left, area.Right - size.Width))),
                Math.Max(area.Top, Math.Min(p.Y, Math.Max(area.Top, area.Bottom - size.Height))));
        }

        private static double Overlap(Point p, Size size, Rect obstacle, bool present)
        {
            if (!present) return 0;
            double width = Math.Max(0, Math.Min(p.X + size.Width, obstacle.Right) - Math.Max(p.X, obstacle.Left));
            double height = Math.Max(0, Math.Min(p.Y + size.Height, obstacle.Bottom) - Math.Max(p.Y, obstacle.Top));
            return width * height;
        }

        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool Valid(Rect r)
        { return !r.IsEmpty && Finite(r.X) && Finite(r.Y) && Finite(r.Width) && Finite(r.Height) && Finite(r.Right) && Finite(r.Bottom); }
    }
}
