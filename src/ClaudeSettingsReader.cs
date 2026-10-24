using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using Condition = System.Windows.Automation.Condition;

namespace Yuqiao
{
    // Local dictionary of observed settings chrome. Exact-name queries only;
    // never reads setting values, account fields or unknown text, and no network.
    public static class ClaudeSettingsReader
    {
        private static readonly Dictionary<string, string> Tabs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"設定を検索", "搜索设置"}, {"環境設定", "偏好设置"}, {"一般", "通用"},
            {"アカウント", "账户"}, {"プライバシー", "隐私"}, {"請求", "账单"},
            {"機能", "功能"}, {"メモリ", "记忆"}, {"振り返り", "回顾"}, {"時間と集中", "时间与专注"},
            {"Claude Code", "代码工具"}, {"このコンピュータ", "本机"}, {"システム", "系统"},
            {"拡張機能", "扩展"}, {"開発者", "开发者"}, {"カスタマイズ", "自定义"},
            {"Skills", "技能"}, {"コネクタ", "连接器"}, {"プラグイン", "插件"},
            {"Search settings", "搜索设置"}, {"Preferences", "偏好设置"}, {"General", "通用"},
            {"Account", "账户"}, {"Privacy", "隐私"}, {"Billing", "账单"},
            {"Features", "功能"}, {"Memory", "记忆"}, {"Reflection", "回顾"}, {"Time and focus", "时间与专注"},
            {"This computer", "本机"}, {"System", "系统"}, {"Extensions", "扩展"}, {"Developer", "开发者"},
            {"Customization", "自定义"}, {"Connectors", "连接器"}, {"Plugins", "插件"}
        };
        private static readonly Dictionary<string, string> Sections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"外観", "外观"}, {"音声", "语音"}, {"通知", "通知"},
            {"Appearance", "外观"}, {"Voice", "语音"}, {"Notifications", "通知"}
        };
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"テーマ", "主题"},
            {"インターフェースフォント", "界面字体"}, {"回答のフォント", "回答字体"},
            {"会話の文字サイズ", "对话字号"}, {"トランスクリプトの幅", "对话区域宽度"}, {"モーション", "动态效果"},
            {"言語", "语音语言"}, {"スタイル", "语音风格"}, {"速度", "语速"},
            {"応答の完了", "回答完成通知"}, {"メンションと返信", "提及与回复"},
            {"自分の Artifacts へのコメント", "自己作品的评论"}, {"共有された Artifacts", "作品分享通知"},
            {"Theme", "主题"},
            {"Interface font", "界面字体"}, {"Response font", "回答字体"}, {"Conversation font size", "对话字号"},
            {"Transcript width", "对话区域宽度"}, {"Motion", "动态效果"}, {"Language", "语音语言"},
            {"Style", "语音风格"}, {"Speed", "语速"}, {"Response completion", "回答完成通知"},
            {"Mentions and replies", "提及与回复"}, {"Comments on your Artifacts", "自己作品的评论"},
            {"Shared Artifacts", "作品分享通知"}
        };
        private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"Claudeが応答を完了したときに通知を受け取ります。長時間かかるタスクに便利です。", "回答完成时提醒，适合耗时任务。"},
            {"Claude が応答を完了したときに通知を受け取ります。長時間かかるタスクに便利です。", "回答完成时提醒，适合耗时任务。"},
            {"Artifacts で誰かに @メンションされたとき、または参加しているコメントスレッドに返信があったときに、メールで通知を受け取ります。", "作品中被提及，或参与的评论收到回复时，发送邮件提醒。"},
            {"自分が所有する Artifacts に誰かがコメントしたときに、メンションされていなくてもメールを受け取ります。変更するまでは「メンションと返信」の設定に従います。", "自己作品收到评论时发送邮件；修改前沿用“提及与回复”设置。"},
            {"Artifacts が共有されたときにメールで通知を受け取ります。", "作品被分享时发送邮件提醒。"}
        };
        private static readonly Dictionary<string, string> KnownOptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"デフォルト · システム", "默认·系统"}, {"インターフェースに合わせる", "跟随界面"},
            {"中", "中等"}, {"狭い", "窄"}, {"日本語", "日语"}, {"選択", "请选择"}, {"標準", "标准"},
            {"システム", "跟随系统"}, {"ライト", "浅色"}, {"ダーク", "深色"},
            {"System", "跟随系统"}, {"Light", "浅色"}, {"Dark", "深色"},
            {"Default · System", "默认·系统"}, {"Match interface", "跟随界面"}, {"Medium", "中等"},
            {"Narrow", "窄"}, {"Japanese", "日语"}, {"Select", "请选择"}, {"Standard", "标准"}
        };

        internal sealed class Choice
        {
            internal string Id = "", Chinese = "";
            internal Rect TextBounds = Rect.Empty, ControlBounds = Rect.Empty;
        }
        internal sealed class ChoiceControl
        {
            internal string Id = "";
            internal Rect Bounds = Rect.Empty;
        }

        public static bool TryRead(AutomationElement document, ClaudeChromeSnapshot snapshot)
        {
            if (document == null || snapshot == null) return false;
            AutomationElement dialog = FindDialog(document);
            if (dialog == null) return false;
            snapshot.SettingsVisible = true;
            snapshot.SettingsBounds = dialog.Current.BoundingRectangle;
            snapshot.Settings.Clear();
            snapshot.SidebarVisible = snapshot.MenuVisible = false;
            snapshot.SidebarBounds = snapshot.MenuBounds = Rect.Empty;
            snapshot.Sidebar.Clear(); snapshot.AccountMenu.Clear();
            snapshot.MenuHasUnsupportedItems = false;
            AutomationElement navigation = FindNavigation(dialog);
            if (navigation != null) ReadKnown(navigation, Tabs, "settings-tab", null, snapshot.Settings);
            SetPage(document, dialog, navigation, snapshot);
            ReadKnown(dialog, Sections, "section", navigation, snapshot.Settings);
            List<ClaudeChromeItem> labels = ReadLabels(dialog, navigation);
            List<Choice> choices = new List<Choice>(); List<ChoiceControl> controls = new List<ChoiceControl>();
            if (GeneralPage(document)) ReadChoices(dialog, navigation, choices, controls);
            snapshot.Settings.AddRange(MergeRows(labels, choices, controls));
            ReadKnown(dialog, Descriptions, "description", navigation, snapshot.Settings);
            snapshot.Settings.Sort(delegate(ClaudeChromeItem first, ClaudeChromeItem second)
            {
                bool firstTab = first.Kind == "settings-tab", secondTab = second.Kind == "settings-tab";
                if (firstTab != secondTab) return firstTab ? -1 : 1;
                int y = first.Bounds.Top.CompareTo(second.Bounds.Top); return y != 0 ? y : first.Bounds.Left.CompareTo(second.Bounds.Left);
            });
            snapshot.Status = "设置中文辅助 · 当前支持通用页的外观、语音和通知；未知选项不会读取或外发。";
            return true;
        }
        private static void SetPage(AutomationElement document, AutomationElement dialog, AutomationElement navigation, ClaudeChromeSnapshot snapshot)
        {
            snapshot.SettingsPageKey = "unknown"; snapshot.SettingsPageChinese = "设置";
            object supported; Uri url;
            if (document.TryGetCurrentPattern(ValuePattern.Pattern, out supported)
                && Uri.TryCreate(((ValuePattern)supported).Current.Value, UriKind.Absolute, out url))
            {
                string fragment = url.Fragment;
                if (fragment.StartsWith("#settings/", StringComparison.Ordinal))
                {
                    string page = fragment.Substring(10).Split('?', '/', '&')[0];
                    Dictionary<string, string> pages = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        {"general", "通用"}, {"account", "账户"}, {"privacy", "隐私"}, {"billing", "账单"},
                        {"capabilities", "功能"}, {"features", "功能"}, {"memory", "记忆"}, {"reflection", "回顾"},
                        {"time-and-focus", "时间与专注"}, {"code", "代码工具"}, {"desktop", "本机"},
                        {"system", "系统"}, {"extensions", "扩展"}, {"developer", "开发者"},
                        {"customization", "自定义"}, {"skills", "技能"}, {"connectors", "连接器"}, {"plugins", "插件"}
                    };
                    string chinese;
                    if (pages.TryGetValue(page, out chinese)) { snapshot.SettingsPageKey = page; snapshot.SettingsPageChinese = chinese; }
                }
            }
            Rect bounds = dialog.Current.BoundingRectangle;
            double left = navigation == null ? bounds.Left : Math.Max(bounds.Left, navigation.Current.BoundingRectangle.Right);
            double top = navigation == null ? bounds.Top : Math.Max(bounds.Top, navigation.Current.BoundingRectangle.Top);
            snapshot.SettingsContentBounds = left < bounds.Right && top < bounds.Bottom
                ? new Rect(left, top, bounds.Right - left, bounds.Bottom - top) : bounds;
        }
        private static bool GeneralPage(AutomationElement document)
        {
            object supported; Uri url;
            return document.TryGetCurrentPattern(ValuePattern.Pattern, out supported)
                && Uri.TryCreate(((ValuePattern)supported).Current.Value, UriKind.Absolute, out url)
                && (url.Fragment == "#settings/general" || url.Fragment.StartsWith("#settings/general?", StringComparison.Ordinal));
        }
        private static List<ClaudeChromeItem> ReadLabels(AutomationElement dialog, AutomationElement navigation)
        {
            List<ClaudeChromeItem> output = new List<ClaudeChromeItem>();
            List<string> names = new List<string>(Labels.Keys);
            List<AutomationElement> nodes = new List<AutomationElement>();
            foreach (AutomationElement node in dialog.FindAll(TreeScope.Descendants, Names(names.ToArray()))) nodes.Add(node);
            nodes.Sort(delegate(AutomationElement first, AutomationElement second)
            { return TextPriority(first).CompareTo(TextPriority(second)); });
            foreach (AutomationElement node in nodes)
            {
                if (!Visible(node) || (navigation != null && Within(node, navigation)) || InsideValueControl(node, dialog)) continue;
                ControlType type = node.Current.ControlType;
                if (type == ControlType.Edit || type == ControlType.ComboBox || type == ControlType.Slider || type == ControlType.Spinner
                    || type == ControlType.Document || type == ControlType.Window || type == ControlType.Pane) continue;
                string original = node.Current.Name ?? "", chinese;
                if (!Labels.TryGetValue(original, out chinese)) continue;
                AutomationElement anchor = TextAnchor(node, original);
                if (anchor == null) continue;
                Rect bounds = anchor.Current.BoundingRectangle;
                bool duplicate = false;
                // A switch can repeat its label at the far right of the same
                // row. Text nodes were sorted first: retain that source anchor
                // and do not add the named switch as another translated row.
                foreach (ClaudeChromeItem existing in output)
                    if (existing.Original == original) { duplicate = true; break; }
                if (duplicate) continue;
                output.Add(new ClaudeChromeItem { Id = Runtime(anchor), Original = original, LocalChinese = chinese,
                    Kind = "setting", NeedsTranslation = false, Bounds = bounds });
            }
            output.Sort(delegate(ClaudeChromeItem first, ClaudeChromeItem second) { return first.Bounds.Top.CompareTo(second.Bounds.Top); });
            return output;
        }
        private static int TextPriority(AutomationElement node)
        { ControlType type = node.Current.ControlType; return type == ControlType.Text || type == ControlType.Header ? 0 : 1; }
        private static AutomationElement TextAnchor(AutomationElement node, string original)
        {
            if (TextPriority(node) == 0) return node;
            AutomationElement text = node.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text), Names(new[] { original })));
            if (text != null && Visible(text)) return text;
            // Do not use a content Group spanning a label and its description as
            // the row's Y anchor; controls without a separate Text may use self.
            return node.Current.ControlType == ControlType.Group ? null : node;
        }
        private static void ReadChoices(AutomationElement dialog, AutomationElement navigation, List<Choice> choices, List<ChoiceControl> controls)
        {
            Condition controlTypes = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton));
            foreach (AutomationElement control in dialog.FindAll(TreeScope.Descendants, controlTypes))
                if (Visible(control) && (navigation == null || !Within(control, navigation)))
                    controls.Add(new ChoiceControl { Id = Runtime(control), Bounds = control.Current.BoundingRectangle });
            List<string> names = new List<string>(KnownOptions.Keys);
            List<AutomationElement> nodes = new List<AutomationElement>();
            foreach (AutomationElement node in dialog.FindAll(TreeScope.Descendants, new AndCondition(
                new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton)), Names(names.ToArray())))) nodes.Add(node);
            nodes.Sort(delegate(AutomationElement first, AutomationElement second) { return TextPriority(first).CompareTo(TextPriority(second)); });
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AutomationElement node in nodes)
            {
                if (!Visible(node) || (navigation != null && Within(node, navigation))) continue;
                AutomationElement owner = ChoiceOwner(node, dialog);
                if (owner == null || !Visible(owner)) continue;
                string original = node.Current.Name ?? "", chinese;
                if (!KnownOptions.TryGetValue(original, out chinese)) continue;
                string identity = Runtime(owner);
                // Per control, not globally by text: the theme and Motion can
                // both show System and must each retain their own current row.
                if (!seen.Add(identity + "|" + original)) continue;
                choices.Add(new Choice { Id = identity, Chinese = chinese, TextBounds = node.Current.BoundingRectangle, ControlBounds = owner.Current.BoundingRectangle });
            }
        }
        private static AutomationElement ChoiceOwner(AutomationElement node, AutomationElement boundary)
        {
            AutomationElement current = node;
            for (int depth = 0; current != null && depth < 24; depth++, current = TreeWalker.RawViewWalker.GetParent(current))
            {
                if (Automation.Compare(current, boundary)) return null;
                ControlType type = current.Current.ControlType;
                if (type == ControlType.Edit) return null;
                if (type == ControlType.ComboBox || type == ControlType.RadioButton) return current;
            }
            return null;
        }
        internal static List<ClaudeChromeItem> MergeRows(List<ClaudeChromeItem> labels, List<Choice> choices, List<ChoiceControl> controls)
        {
            List<ClaudeChromeItem> rows = new List<ClaudeChromeItem>();
            List<List<Choice>> attached = new List<List<Choice>>();
            List<HashSet<string>> expected = new List<HashSet<string>>();
            List<double> controlRight = new List<double>();
            for (int index = 0; index < labels.Count; index++) { attached.Add(new List<Choice>()); expected.Add(new HashSet<string>(StringComparer.Ordinal)); controlRight.Add(labels[index].Bounds.Right); }
            foreach (ChoiceControl control in controls)
            { int index = NearestLabel(labels, control.Bounds); if (index >= 0) { expected[index].Add(control.Id); controlRight[index] = Math.Max(controlRight[index], control.Bounds.Right); } }
            foreach (Choice choice in choices)
            { int index = NearestLabel(labels, choice.TextBounds); if (index >= 0) attached[index].Add(choice); }
            for (int index = 0; index < labels.Count; index++)
            {
                ClaudeChromeItem label = labels[index];
                List<Choice> values = attached[index];
                values.Sort(delegate(Choice first, Choice second) { return first.TextBounds.Left.CompareTo(second.TextBounds.Left); });
                List<string> text = new List<string>(); HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
                double right = controlRight[index];
                foreach (Choice value in values)
                { text.Add(value.Chinese); found.Add(value.Id); right = Math.Max(right, value.ControlBounds.Right); }
                bool missing = false; foreach (string id in expected[index]) if (!found.Contains(id)) missing = true;
                if (missing) text.Add("未匹配选项");
                string chinese = label.LocalChinese + (text.Count > 0 ? "：" + String.Join(" / ", text.ToArray()) : "");
                rows.Add(new ClaudeChromeItem { Id = label.Id, Original = label.Original, Kind = "setting", LocalChinese = chinese, NeedsTranslation = false,
                    Bounds = new Rect(label.Bounds.Left, label.Bounds.Top, Math.Max(label.Bounds.Width, right - label.Bounds.Left), label.Bounds.Height) });
            }
            return rows;
        }
        private static int NearestLabel(List<ClaudeChromeItem> labels, Rect option)
        {
            if (option.IsEmpty) return -1;
            int selected = -1; double best = Double.PositiveInfinity;
            for (int index = 0; index < labels.Count; index++)
            {
                Rect label = labels[index].Bounds;
                if (label.IsEmpty || option.Left < label.Left - 4) continue;
                double gap = Math.Abs(option.Top + option.Height / 2 - label.Top - label.Height / 2);
                double maximum = Math.Max(20, Math.Min(34, (option.Height + label.Height) / 2));
                if (gap > maximum) continue;
                double score = gap + Math.Abs(option.Left - label.Right) * 0.0001;
                if (score < best) { best = score; selected = index; }
            }
            return selected;
        }
        private static AutomationElement FindDialog(AutomationElement document)
        {
            AutomationElement selected = null; int count = 0;
            foreach (AutomationElement candidate in document.FindAll(TreeScope.Descendants, Names(new[] { "設定", "Settings" })))
            {
                if (!Visible(candidate)) continue;
                // A settings navigation node shares the dialog's name; only the
                // enclosing dialog has both the close command and nav signature.
                if (candidate.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    Names(new[] { "閉じる", "Close" }))) == null) continue;
                if (FindNavigation(candidate) == null) continue;
                selected = candidate; count++;
            }
            return count == 1 ? selected : null;
        }
        private static AutomationElement FindNavigation(AutomationElement dialog)
        {
            AutomationElement selected = null; int count = 0;
            foreach (AutomationElement candidate in dialog.FindAll(TreeScope.Descendants, Names(new[] { "設定", "Settings" })))
            {
                if (!Visible(candidate)) continue;
                if (candidate.FindFirst(TreeScope.Descendants, Names(new[] { "一般", "General" })) == null
                    || candidate.FindFirst(TreeScope.Descendants, Names(new[] { "プライバシー", "Privacy" })) == null) continue;
                selected = candidate; count++;
            }
            return count == 1 ? selected : null;
        }
        private static void ReadKnown(AutomationElement root, Dictionary<string, string> dictionary, string kind,
            AutomationElement excluded, List<ClaudeChromeItem> output)
        {
            List<string> keys = new List<string>(dictionary.Keys);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AutomationElement node in root.FindAll(TreeScope.Descendants, Names(keys.ToArray())))
            {
                if (!Visible(node) || (excluded != null && Within(node, excluded))) continue;
                ControlType type = node.Current.ControlType;
                if (type == ControlType.ComboBox || type == ControlType.Slider || type == ControlType.Spinner
                    || type == ControlType.Document || type == ControlType.Window || type == ControlType.Pane) continue;
                if (InsideValueControl(node, root)) continue;
                if (kind == "settings-tab" && type != ControlType.Button && type != ControlType.Hyperlink
                    && type != ControlType.TabItem && type != ControlType.TreeItem && type != ControlType.RadioButton && type != ControlType.Edit) continue;
                // An edit's fixed accessible Name is a label, not its value.
                // Only the known settings-search label may be read from an Edit.
                string name = node.Current.Name ?? "", chinese;
                if (type == ControlType.Edit && name != "設定を検索" && name != "Search settings") continue;
                if (!dictionary.TryGetValue(name, out chinese) || !seen.Add(name)) continue;
                AutomationElement anchor = kind == "section" || kind == "description" ? TextAnchor(node, name) : node;
                if (anchor == null) continue;
                output.Add(new ClaudeChromeItem { Id = Runtime(anchor), Original = name, LocalChinese = chinese,
                    Kind = kind, NeedsTranslation = false, Bounds = anchor.Current.BoundingRectangle });
            }
        }
        private static bool Within(AutomationElement element, AutomationElement ancestor)
        {
            AutomationElement current = element;
            for (int depth = 0; current != null && depth < 24; depth++, current = TreeWalker.RawViewWalker.GetParent(current))
                if (Automation.Compare(current, ancestor)) return true;
            return false;
        }
        private static bool InsideValueControl(AutomationElement element, AutomationElement boundary)
        {
            AutomationElement current = TreeWalker.RawViewWalker.GetParent(element);
            for (int depth = 0; current != null && depth < 24; depth++, current = TreeWalker.RawViewWalker.GetParent(current))
            {
                if (Automation.Compare(current, boundary)) return false;
                ControlType type = current.Current.ControlType;
                if (type == ControlType.ComboBox || type == ControlType.Edit || type == ControlType.Slider || type == ControlType.Spinner) return true;
            }
            return false;
        }
        private static bool Visible(AutomationElement node)
        {
            if (node == null || node.Current.IsOffscreen) return false;
            Rect bounds = node.Current.BoundingRectangle;
            return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0;
        }
        private static Condition Names(string[] values)
        {
            Condition[] conditions = new Condition[values.Length];
            for (int index = 0; index < values.Length; index++) conditions[index] = new PropertyCondition(AutomationElement.NameProperty, values[index]);
            return values.Length == 1 ? conditions[0] : new OrCondition(conditions);
        }
        private static string Runtime(AutomationElement node) { return String.Join(".", Array.ConvertAll(node.GetRuntimeId(), value => value.ToString())); }
    }
}
