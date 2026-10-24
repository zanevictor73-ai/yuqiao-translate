using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;

namespace Yuqiao
{
    public class ChatSnapshot
    {
        public bool Connected;
        public string Status = "";
        public string Title = "";
        public string ConversationKey = "";
        public string Draft = "";
        public string Reply = "";
        public string ReplyId = "";
        public int ReplyOrder;
        public readonly List<ChatUserMessage> UserMessages = new List<ChatUserMessage>();
        public long ReadMs;
    }

    // Read-only: this class never invokes controls, changes focus, or sends keys.
    public class ChatWindowReader
    {
        private const int MaxText = 8000;
        private readonly IntPtr testWindow;
        private readonly string profile = "chatgpt";
        private IntPtr warmedWindow;
        private static readonly Regex TimeOnly = new Regex(@"^\d{1,2}:\d{2}(?::\d{2})?$", RegexOptions.Compiled);
        private delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId,
            ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object result);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hwnd, EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);

        public ChatWindowReader() { }
        public ChatWindowReader(string profile)
        {
            if (string.Equals(profile, "claude", StringComparison.OrdinalIgnoreCase)) this.profile = "claude";
            else if (string.Equals(profile, "claude-web", StringComparison.OrdinalIgnoreCase)) this.profile = "claude-web";
            else if (string.Equals(profile, "deepseek", StringComparison.OrdinalIgnoreCase)) this.profile = "deepseek";
            else if (!string.Equals(profile, "chatgpt", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unknown chat profile", "profile");
        }
        public ChatWindowReader(IntPtr testWindow) { this.testWindow = testWindow; }

        public ChatSnapshot Read()
        {
            if (profile == "deepseek" && testWindow == IntPtr.Zero) return GenericDesktopAdapter.ReadDeepSeek();
            Stopwatch watch = Stopwatch.StartNew();
            ChatSnapshot snapshot = new ChatSnapshot();
            try
            {
                IntPtr hwnd = SelectWindow(snapshot);
                if (hwnd == IntPtr.Zero) return snapshot;
                if (!IsWindow(hwnd)) { snapshot.Status = "聊天窗口已关闭"; return snapshot; }
                if (warmedWindow != hwnd)
                {
                    WarmAccessibility(hwnd);
                    warmedWindow = hwnd;
                }

                AutomationElement window = AutomationElement.FromHandle(hwnd);
                if (profile == "claude" || profile == "claude-web")
                {
                    ReadClaude(window, hwnd, snapshot);
                    return snapshot;
                }
                AutomationElement document = window.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "RootWebArea"));
                if (testWindow == IntPtr.Zero && document == null)
                {
                    WarmAccessibility(hwnd);
                    document = window.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "RootWebArea"));
                }
                AutomationElement searchRoot = document ?? window;
                snapshot.Title = document != null ? SafeName(document) : SafeName(window);
                snapshot.ConversationKey = BuildConversationKey(document ?? window, hwnd, profile);

                AutomationElement conversation = null;
                AutomationElement composer = null;
                if (testWindow != IntPtr.Zero)
                {
                    conversation = searchRoot.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "YuqiaoDemoConversation"));
                    composer = searchRoot.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "YuqiaoDemoComposer"));
                }
                if (conversation == null)
                {
                    AutomationElementCollection matches = searchRoot.FindAll(TreeScope.Descendants,
                        new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group),
                            new PropertyCondition(AutomationElement.NameProperty, "对话")));
                    if (matches.Count == 1) conversation = matches[0];
                    else if (matches.Count > 1)
                    {
                        snapshot.Status = "发现多个对话区域，未自动选择";
                        return snapshot;
                    }
                }
                if (composer == null)
                {
                    List<AutomationElement> edits = FindComposers(conversation ?? searchRoot);
                    if (edits.Count == 0 && conversation != null) edits = FindComposers(searchRoot);
                    int visibleCount = 0;
                    foreach (AutomationElement edit in edits)
                    {
                        if (!edit.Current.IsOffscreen) { composer = edit; visibleCount++; }
                    }
                    if (visibleCount > 1)
                    {
                        snapshot.Status = "发现多个输入框，未自动选择";
                        return snapshot;
                    }
                    if (visibleCount == 0 && edits.Count == 1) composer = edits[0];
                }
                if (conversation == null || composer == null)
                {
                    snapshot.Status = conversation == null ? "未找到当前对话区域" : "未找到聊天输入框";
                    return snapshot;
                }

                snapshot.Draft = ReadDraft(composer);
                List<Node> nodes = CacheConversation(conversation);
                ExtractLatestReply(nodes, snapshot);
                snapshot.Connected = true;
                snapshot.Status = testWindow != IntPtr.Zero ? "已连接测试窗口" : "已连接 ChatGPT 当前对话";
            }
            catch (ElementNotAvailableException) { snapshot.Status = "聊天窗口正在切换，稍后重试"; }
            catch (InvalidOperationException) { snapshot.Status = "当前界面暂时不可读取"; }
            catch (COMException) { snapshot.Status = "聊天界面暂未提供可访问文字"; }
            catch (Exception) { snapshot.Status = "暂时无法读取聊天窗口"; }
            finally { snapshot.ReadMs = watch.ElapsedMilliseconds; }
            return snapshot;
        }

        private IntPtr SelectWindow(ChatSnapshot snapshot)
        {
            if (testWindow != IntPtr.Zero) return testWindow;
            IntPtr result = IntPtr.Zero;
            int count = 0;
            if (profile == "claude-web") return SelectClaudeBrowserWindow(snapshot);
            string processName = profile == "claude" ? "claude" : "ChatGPT";
            string displayName = profile == "claude" ? "Claude" : "ChatGPT";
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        count++;
                        result = process.MainWindowHandle;
                    }
                }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            if (count == 1) return result;
            snapshot.Status = count == 0 ? "请先打开 " + displayName + " 桌面对话" : "发现多个 " + displayName + " 窗口，请保留一个窗口";
            return IntPtr.Zero;
        }

        private IntPtr SelectClaudeBrowserWindow(ChatSnapshot snapshot)
        {
            IntPtr selected = IntPtr.Zero;
            int count = 0;
            foreach (string browser in new string[] { "msedge", "chrome" })
            foreach (Process process in Process.GetProcessesByName(browser))
            {
                try
                {
                    IntPtr hwnd = process.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    WarmAccessibility(hwnd);
                    AutomationElement document = FindClaudeDocument(AutomationElement.FromHandle(hwnd));
                    if (document != null && FindComposers(document).Count == 1) { selected = hwnd; count++; }
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            if (count == 1) return selected;
            snapshot.Status = count == 0 ? "请在 Edge 或 Chrome 当前标签打开 Claude 对话" : "发现多个 Claude 网页窗口，请保留一个";
            return IntPtr.Zero;
        }

        private void ReadClaude(AutomationElement window, IntPtr hwnd, ChatSnapshot snapshot)
        {
            AutomationElement document = FindClaudeDocument(window);
            if (document == null) { snapshot.Status = "未找到唯一可读取的 Claude 对话页面"; return; }
            snapshot.Title = SafeName(document);
            snapshot.ConversationKey = BuildConversationKey(document, hwnd, profile);
            List<AutomationElement> composers = FindComposers(document);
            AutomationElement composer = null;
            int visible = 0;
            foreach (AutomationElement candidate in composers)
                if (!candidate.Current.IsOffscreen) { composer = candidate; visible++; }
            if (visible > 1 || (visible == 0 && composers.Count != 1))
            { snapshot.Status = "未找到唯一的 Claude 输入框"; return; }
            if (visible == 0) composer = composers[0];
            snapshot.Draft = ReadDraft(composer);
            AutomationElementCollection groups = document.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group));
            AutomationElement conversation = null;
            int feeds = 0;
            foreach (AutomationElement group in groups)
            {
                string name = group.Current.Name ?? "";
                if (name == "チャットメッセージ" || name == "Chat messages" || name == "聊天消息" || name == "对话消息")
                { conversation = group; feeds++; }
            }
            if (feeds > 1) { snapshot.Status = "发现多个 Claude 消息区域，未自动选择"; return; }
            if (conversation != null) ExtractClaudeReply(CacheConversation(conversation), snapshot);
            else
            {
                Uri url;
                if (!Uri.TryCreate(DocumentUrl(document), UriKind.Absolute, out url) || url.AbsolutePath != "/new")
                { snapshot.Status = "未找到 Claude 聊天消息区域"; return; }
            }
            snapshot.Connected = true;
            snapshot.Status = profile == "claude-web" ? "已连接 Claude 网页对话" : "已连接 Claude 客户端对话";
        }

        private static AutomationElement FindClaudeDocument(AutomationElement window)
        {
            AutomationElement selected = null;
            int count = 0;
            AutomationElementCollection documents = window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            foreach (AutomationElement document in documents)
            {
                Uri url;
                if (!Uri.TryCreate(DocumentUrl(document), UriKind.Absolute, out url)) continue;
                if (url.Scheme != "https" || !string.Equals(url.Host, "claude.ai", StringComparison.OrdinalIgnoreCase)) continue;
                if (url.AbsolutePath != "/new" && !url.AbsolutePath.StartsWith("/chat/", StringComparison.Ordinal)) continue;
                count++;
                selected = document;
            }
            return count == 1 ? selected : null;
        }

        private static string DocumentUrl(AutomationElement document)
        {
            object pattern;
            if (document.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                return ((ValuePattern)pattern).Current.Value ?? "";
            return "";
        }

        private static string BuildConversationKey(AutomationElement document, IntPtr hwnd, string profile)
        {
            string identity = DocumentUrl(document);
            Uri url;
            if (Uri.TryCreate(identity, UriKind.Absolute, out url)) identity = url.GetLeftPart(UriPartial.Path);
            if (identity.Length == 0) identity = "runtime:" + RuntimeKey(document);
            return profile + "|" + hwnd.ToInt64().ToString() + "|" + identity;
        }

        private static string RuntimeKey(AutomationElement element)
        {
            int[] runtime = element.GetRuntimeId();
            string[] parts = new string[runtime.Length];
            for (int i = 0; i < runtime.Length; i++) parts[i] = runtime[i].ToString();
            return string.Join(".", parts);
        }

        private static void WarmAccessibility(IntPtr hwnd)
        {
            RequestAccessibility(hwnd);
            EnumChildWindows(hwnd, delegate(IntPtr child, IntPtr unused)
            {
                RequestAccessibility(child);
                return true;
            }, IntPtr.Zero);
        }

        private static void RequestAccessibility(IntPtr hwnd)
        {
            object accessible = null;
            try
            {
                Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
                AccessibleObjectFromWindow(hwnd, 0xFFFFFFFC, ref iid, out accessible);
            }
            catch (COMException) { }
            finally
            {
                if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible);
            }
        }

        private static string SafeName(AutomationElement element)
        {
            return element.Current.Name ?? "";
        }

        private static List<AutomationElement> FindComposers(AutomationElement root)
        {
            List<AutomationElement> result = new List<AutomationElement>();
            AutomationElementCollection edits = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            foreach (AutomationElement edit in edits)
            {
                // Focus adds ProseMirror-focused, so ClassName must be token-matched.
                string[] tokens = (edit.Current.ClassName ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                    if (token == "ProseMirror") { result.Add(edit); break; }
            }
            return result;
        }

        private static string ReadDraft(AutomationElement composer)
        {
            object pattern;
            // Chromium may expose its empty-editor placeholder as the value.
            if (composer.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                string value = ((ValuePattern)pattern).Current.Value ?? "";
                if (string.IsNullOrWhiteSpace(value)) return "";
                string name = SafeName(composer);
                if (value.Trim() == name && IsPlaceholder(name)) return "";
                return Limit(value);
            }
            if (composer.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
            {
                string value = ((TextPattern)pattern).DocumentRange.GetText(MaxText) ?? "";
                if (string.IsNullOrWhiteSpace(value)) return "";
                value = value.TrimEnd('\r', '\n');
                string name = SafeName(composer);
                if (value.Trim() == name && IsPlaceholder(name)) return "";
                return Limit(value);
            }
            // Accessible Name is often a placeholder, so never treat it as a draft.
            return "";
        }

        private static bool IsPlaceholder(string text)
        {
            return text == "和 Codex 一起工作" || text == "询问任何问题" || text == "发送消息给 ChatGPT"
                || text == "Message ChatGPT" || text == "Ask anything" || text == "与 Codex 一起工作"
                || text == "Claude へのプロンプトを入力してください" || text == "Message Claude";
        }

        private sealed class Node
        {
            public AutomationElement Element;
            public string Name;
            public string ClassName;
            public string LocalizedType;
            public ControlType Type;
            public bool Offscreen;
            public Rect Bounds;
            public int Parent;
            public int End;
        }

        private static List<Node> CacheConversation(AutomationElement conversation)
        {
            CacheRequest request = new CacheRequest();
            request.TreeScope = TreeScope.Subtree;
            request.TreeFilter = Automation.ControlViewCondition;
            request.AutomationElementMode = AutomationElementMode.Full;
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.ClassNameProperty);
            request.Add(AutomationElement.LocalizedControlTypeProperty);
            request.Add(AutomationElement.IsOffscreenProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);
            List<Node> result = new List<Node>();
            AutomationElement cached = conversation.GetUpdatedCache(request);
            WalkCached(cached, -1, 0, result);
            return result;
        }

        private static void WalkCached(AutomationElement element, int parent, int depth, List<Node> nodes)
        {
            if (depth > 65) return;
            // Do not silently return an earlier reply if a huge tree was truncated.
            if (nodes.Count >= 16000) throw new InvalidOperationException("Conversation is too large to read safely.");
            Node node = new Node();
            node.Element = element;
            node.Name = element.Cached.Name ?? "";
            node.ClassName = element.Cached.ClassName ?? "";
            node.LocalizedType = element.Cached.LocalizedControlType ?? "";
            node.Type = element.Cached.ControlType;
            node.Offscreen = element.Cached.IsOffscreen;
            node.Bounds = element.Cached.BoundingRectangle;
            node.Parent = parent;
            int index = nodes.Count;
            nodes.Add(node);
            AutomationElementCollection children = element.CachedChildren;
            if (children != null)
                foreach (AutomationElement child in children) WalkCached(child, index, depth + 1, nodes);
            node.End = nodes.Count;
        }

        private static bool IsAssistant(Node node)
        {
            return node.Type == ControlType.Text && (node.Name == "ChatGPT 说：" || node.Name == "ChatGPT said:");
        }

        private static bool IsRole(Node node)
        {
            return IsAssistant(node) || (node.Type == ControlType.Text &&
                (node.Name == "你说：" || node.Name == "You said:" || node.Name == "你说:"));
        }

        private static void ExtractLatestReply(List<Node> nodes, ChatSnapshot snapshot)
        {
            int marker = -1;
            int ordinal = 0;
            for (int i = 0; i < nodes.Count; i++)
                if (IsAssistant(nodes[i])) { marker = i; ordinal++; }
            if (marker < 0) return;
            ExtractReplyAtMarker(nodes, snapshot, marker, ordinal, 0);
        }

        private static void ExtractClaudeReply(List<Node> nodes, ChatSnapshot snapshot)
        {
            int marker = -1;
            int ordinal = 0;
            int messageOrder = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (node.Type != ControlType.Text || node.ClassName.IndexOf("sr-only", StringComparison.Ordinal) < 0) continue;
                string name = node.Name;
                if (name.StartsWith("あなたの入力:", StringComparison.Ordinal)
                    || name.StartsWith("あなたの入力：", StringComparison.Ordinal)
                    || name.StartsWith("Your input:", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("You said:", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("你的输入", StringComparison.Ordinal)
                    || name.StartsWith("您的輸入", StringComparison.Ordinal))
                {
                    messageOrder++;
                    int userArticle = FindArticle(nodes, i);
                    if (userArticle > 0)
                    {
                        ChatSnapshot user = new ChatSnapshot();
                        ExtractReplyAtMarker(nodes, user, i, messageOrder, nodes[userArticle].End);
                        snapshot.UserMessages.Add(new ChatUserMessage { Id="claude-user:"+messageOrder.ToString(), Text=user.Reply, Order=messageOrder });
                    }
                    continue;
                }
                if (name.StartsWith("Claude の応答：", StringComparison.Ordinal)
                    || name.StartsWith("Claude の応答:", StringComparison.Ordinal)
                    || name.StartsWith("Claude said", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Claude's response", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Claude’s response", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Claude 的回复", StringComparison.Ordinal)
                    || name.StartsWith("Claude 的回覆", StringComparison.Ordinal))
                { marker = i; ordinal++; snapshot.ReplyOrder = ++messageOrder; }
            }
            if (marker < 0) return;
            int article = nodes[marker].Parent;
            while (article > 0)
            {
                string type = nodes[article].LocalizedType;
                if (nodes[article].Type == ControlType.Group && (type == "文章" || type.Equals("article", StringComparison.OrdinalIgnoreCase))) break;
                article = nodes[article].Parent;
            }
            if (article <= 0) return;
            ExtractReplyAtMarker(nodes, snapshot, marker, ordinal, nodes[article].End);
            snapshot.ReplyId = "claude-assistant:"+snapshot.ReplyOrder.ToString();
        }

        private static int FindArticle(List<Node> nodes, int marker)
        {
            int article=nodes[marker].Parent;
            while(article>0){string type=nodes[article].LocalizedType;
                if(nodes[article].Type==ControlType.Group&&(type=="文章"||type.Equals("article",StringComparison.OrdinalIgnoreCase)))return article;
                article=nodes[article].Parent;
            }
            return -1;
        }

        private static void ExtractReplyAtMarker(List<Node> nodes, ChatSnapshot snapshot, int marker, int ordinal, int explicitEnd)
        {

            int end = explicitEnd > 0 ? explicitEnd : nodes.Count;
            // Streaming/commentary messages live in their own Group. Scope to that
            // message so adjacent execution status and tools are never collected.
            int ancestor = nodes[marker].Parent;
            while (explicitEnd == 0 && ancestor > 0)
            {
                Node parent = nodes[ancestor];
                if (parent.Type == ControlType.Group)
                {
                    end = parent.End;
                    break;
                }
                ancestor = parent.Parent;
            }
            StringBuilder text = new StringBuilder();
            Rect previousBounds = Rect.Empty;
            bool previousBlock = false;
            for (int i = marker + 1; i < end && text.Length < MaxText; i++)
            {
                Node node = nodes[i];
                if (explicitEnd == 0 && IsRole(node)) break;
                if (node.Type == ControlType.Button)
                {
                    if (explicitEnd == 0 && IsMessageAction(node.Name)) break;
                    i = Math.Max(i, node.End - 1);
                    continue;
                }
                if (node.Type == ControlType.Edit || node.Type == ControlType.StatusBar || node.Type == ControlType.Image
                    || node.Type == ControlType.ToolBar || node.Type == ControlType.Menu)
                {
                    i = Math.Max(i, node.End - 1);
                    continue;
                }
                if (node.Type != ControlType.Text && node.Type != ControlType.ListItem
                    && node.Type != ControlType.DataItem && node.Type != ControlType.Hyperlink
                    && node.Type != ControlType.HeaderItem) continue;
                string content = node.Name;
                if (content.Trim().Length == 0 || (explicitEnd == 0 && IsInterfaceText(content.Trim()))) continue;
                bool block = node.Type == ControlType.ListItem || node.Type == ControlType.DataItem;
                if (text.Length != 0)
                {
                    bool newParagraph = block || previousBlock;
                    if (!previousBounds.IsEmpty && !node.Bounds.IsEmpty && node.Bounds.Top > previousBounds.Bottom + 3)
                        newParagraph = true;
                    if (newParagraph && text[text.Length - 1] != '\n') text.AppendLine();
                }
                text.Append(content);
                previousBounds = node.Bounds;
                previousBlock = block;
                // List and table item Name contains its full sentence. Children can
                // contain just bold fragments or the same link and must not repeat.
                if (node.Type != ControlType.Text) i = Math.Max(i, node.End - 1);
            }
            snapshot.Reply = Limit(text.ToString().Trim());
            snapshot.ReplyId = RuntimeKey(nodes[marker].Element) + ":" + ordinal.ToString();
        }

        private static bool IsMessageAction(string name)
        {
            return name == "复制" || name == "复制消息" || name == "朗读" || name == "评价回复"
                || name == "更多操作" || name == "Copy" || name == "Copy message"
                || name == "Read aloud" || name == "More actions";
        }

        private static bool IsInterfaceText(string text)
        {
            return TimeOnly.IsMatch(text) || text == "最新一条回复" || text == "开始工作"
                || text == "已完成" || text.StartsWith("正在运行 ", StringComparison.Ordinal)
                || text.StartsWith("已运行 ", StringComparison.Ordinal)
                || text.StartsWith("已处理 ", StringComparison.Ordinal)
                || text.StartsWith("用时 ", StringComparison.Ordinal);
        }

        private static string Limit(string text)
        {
            if (text.Length <= MaxText) return text;
            int length = MaxText;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length);
        }
    }
}
