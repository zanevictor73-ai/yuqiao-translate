using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace Yuqiao
{
    // Fills a draft only. This module has no send, key, click or focus API.
    public static class ClaudeComposer
    {
        public static void FillJapanese(string profile, string expectedConversationKey, string japanese)
        { FillJapanese(profile, expectedConversationKey, japanese, null, CancellationToken.None); }

        public static void FillJapanese(string profile, string expectedConversationKey, string japanese, string previousOwnedDraft, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!string.Equals(profile, "claude", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("目前只支持填入 Claude 客户端，请先选择 Claude 客户端。");
            if (string.IsNullOrWhiteSpace(expectedConversationKey))
                throw new InvalidOperationException("尚未确认目标会话，请先连接 Claude 当前对话。");
            if (string.IsNullOrWhiteSpace(japanese))
                throw new InvalidOperationException("译文为空，请先完成翻译。");
            if (japanese.Length > 5000)
                throw new InvalidOperationException("译文超过 5000 个字符，请缩短后再填入。");
            string desired = japanese.Trim();
            ChatWindowReader reader = new ChatWindowReader("claude");
            try
            {
                ChatSnapshot first = reader.Read();
                ValidateSnapshot(first, expectedConversationKey, desired, previousOwnedDraft);
                ClaudeTarget target = ClaudeTarget.FindUnique(expectedConversationKey);
                AutomationElement composer = target.FindVisibleComposer();
                object supported;
                if (!composer.TryGetCurrentPattern(ValuePattern.Pattern, out supported))
                    throw new InvalidOperationException("Claude 输入框暂不支持安全填入，请手动粘贴译文。");
                ValuePattern value = (ValuePattern)supported;
                if (value.Current.IsReadOnly || !composer.Current.IsEnabled)
                    throw new InvalidOperationException("Claude 输入框当前不可编辑，请关闭弹窗或等待回复完成后重试。");

                string before = ClaudeTarget.CleanDraft(value.Current.Value, composer.Current.Name);
                RejectConflict(before, desired, previousOwnedDraft);
                // Re-read after locating the element so a navigation or a newly typed
                // draft during discovery cannot silently change the target.
                cancellation.ThrowIfCancellationRequested();
                ValidateSnapshot(reader.Read(), expectedConversationKey, desired, previousOwnedDraft);
                target.EnsureConversation(expectedConversationKey);
                before = ClaudeTarget.CleanDraft(value.Current.Value, composer.Current.Name);
                RejectConflict(before, desired, previousOwnedDraft);
                cancellation.ThrowIfCancellationRequested();
                if (!string.Equals(before, desired, StringComparison.Ordinal))
                {
                    if (!ClaudeSend.IsInteractionForeground(expectedConversationKey))
                        throw new InvalidOperationException("已切换到其他软件，未填入译文。回到 Claude 后修改中文可重试。");
                    try { value.SetValue(desired); }
                    catch (InvalidOperationException)
                    { throw new InvalidOperationException("Claude 未接受译文填入，未执行发送。请检查输入框后重试。"); }
                    catch (ArgumentException)
                    { throw new InvalidOperationException("Claude 无法接受这段译文，请缩短或检查文本后重试。"); }
                }

                bool verified = false;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    target.EnsureConversation(expectedConversationKey);
                    string actual = ClaudeTarget.CleanDraft(value.Current.Value, composer.Current.Name);
                    if (string.Equals(actual, desired, StringComparison.Ordinal)) { verified = true; break; }
                    if (attempt < 3) Thread.Sleep(40);
                }
                if (!verified)
                    throw new InvalidOperationException("译文填入后的文字与译文不一致，未执行发送。请检查 Claude 输入框后重试。");
                ChatSnapshot after = reader.Read();
                if (!after.Connected || !string.Equals(after.ConversationKey, expectedConversationKey, StringComparison.Ordinal)
                    || !string.Equals((after.Draft ?? "").Trim(), desired, StringComparison.Ordinal))
                    throw new InvalidOperationException("填入后的会话或草稿无法确认，未执行发送。请检查 Claude 当前输入框。");
            }
            catch (ElementNotAvailableException)
            { throw new InvalidOperationException("Claude 窗口已变化，未能确认填入结果。请检查当前会话后重试。"); }
            catch (ElementNotEnabledException)
            { throw new InvalidOperationException("Claude 输入框当前不可编辑，请等待或关闭弹窗后重试。"); }
            catch (COMException)
            { throw new InvalidOperationException("Claude 暂未提供可编辑的输入框，未能确认填入结果。请检查当前输入框。"); }
            catch (UnauthorizedAccessException)
            { throw new InvalidOperationException("Windows 拒绝访问 Claude 输入框，请确认两者使用相同的权限运行。"); }
        }

        private static void ValidateSnapshot(ChatSnapshot snapshot, string expectedKey, string desired, string previousOwnedDraft)
        {
            if (!snapshot.Connected)
                throw new InvalidOperationException("尚未连接 Claude 当前对话：" + snapshot.Status);
            if (!string.Equals(snapshot.ConversationKey, expectedKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Claude 会话已切换，请重新确认当前会话后再填入。");
            RejectConflict((snapshot.Draft ?? "").Trim(), desired, previousOwnedDraft);
        }

        internal static void RejectConflict(string current, string desired, string previousOwnedDraft)
        {
            if (current.Length != 0 && !string.Equals(current, desired, StringComparison.Ordinal)
                && (previousOwnedDraft == null || !string.Equals(current, previousOwnedDraft, StringComparison.Ordinal)))
                throw new InvalidOperationException("Claude 输入框已有其他草稿。为避免覆盖，请先清空该草稿，再填入译文。");
        }
    }

    // Shared target discovery for explicit filling and the separate read-only page aid.
    internal sealed class ClaudeTarget
    {
        internal IntPtr Window;
        internal AutomationElement Document;

        internal static ClaudeTarget FindUnique(string expectedKey)
        {
            IntPtr hwnd = IntPtr.Zero;
            int windows = 0;
            foreach (Process process in Process.GetProcessesByName("claude"))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) { hwnd = process.MainWindowHandle; windows++; }
                }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            if (windows != 1)
                throw new InvalidOperationException(windows == 0 ? "请先打开 Claude 客户端。" : "发现多个 Claude 窗口，请保留一个后重试。");
            AutomationElement root = AutomationElement.FromHandle(hwnd);
            AutomationElement selected = null;
            int documents = 0;
            AutomationElementCollection all = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            foreach (AutomationElement document in all)
            {
                Uri url;
                if (!TryClaudeUrl(document, out url)) continue;
                documents++;
                selected = document;
            }
            if (documents != 1)
                throw new InvalidOperationException("未找到唯一的 Claude 对话页面，请关闭其他页面或弹窗后重试。");
            ClaudeTarget target = new ClaudeTarget();
            target.Window = hwnd;
            target.Document = selected;
            target.EnsureConversation(expectedKey);
            return target;
        }

        internal void EnsureConversation(string expectedKey)
        {
            Uri url;
            if (!TryClaudeUrl(Document, out url))
                throw new InvalidOperationException("Claude 对话页面已变化，请重新连接当前会话。");
            string actualKey = "claude|" + Window.ToInt64().ToString() + "|" + url.GetLeftPart(UriPartial.Path);
            if (!string.Equals(actualKey, expectedKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Claude 会话已切换，请重新确认当前会话后再填入。");
        }

        internal AutomationElement FindVisibleComposer()
        {
            AutomationElement selected = null;
            int count = 0;
            AutomationElementCollection edits = Document.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            foreach (AutomationElement edit in edits)
            {
                if (edit.Current.IsOffscreen || !HasClassToken(edit.Current.ClassName, "ProseMirror")) continue;
                selected = edit;
                count++;
            }
            if (count != 1)
                throw new InvalidOperationException("未找到唯一可见的 Claude 输入框，请关闭弹窗后重试。");
            return selected;
        }

        internal static bool HasClassToken(string className, string expected)
        {
            foreach (string token in (className ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                if (token == expected) return true;
            return false;
        }

        internal static bool TryClaudeUrl(AutomationElement document, out Uri url)
        {
            url = null;
            object supported;
            if (!document.TryGetCurrentPattern(ValuePattern.Pattern, out supported)) return false;
            string value = ((ValuePattern)supported).Current.Value;
            return Uri.TryCreate(value, UriKind.Absolute, out url) && url.Scheme == "https"
                && string.Equals(url.Host, "claude.ai", StringComparison.OrdinalIgnoreCase)
                && (url.AbsolutePath == "/new" || url.AbsolutePath.StartsWith("/chat/", StringComparison.Ordinal));
        }

        internal static string CleanDraft(string value, string accessibleName)
        {
            string text = (value ?? "").Trim();
            if (text == accessibleName && (text == "Claude へのプロンプトを入力してください" || text == "Message Claude")) return "";
            return text;
        }
    }
}
