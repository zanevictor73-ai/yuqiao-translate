using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace Yuqiao
{
    // Native adapters for ChatGPT and the local DeepSeek Harness only.
    // Read/placement are read-only. Fill is an explicit user action and never sends.
    // ChatGPT filling is ValuePattern-only and has not been verified on the live app.
    public static class GenericDesktopAdapter
    {
        private const int MaxText = 8000;
        private static readonly object fillLock = new object();
        private sealed class Target
        {
            internal string Profile;
            internal IntPtr Window;
            internal AutomationElement Root, Document, Composer;
            internal string Key;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct HardwareInput { public uint Message; public ushort Low, High; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public HardwareInput Hardware;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public InputUnion Data; }
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);

        public static ChatSnapshot ReadDeepSeek()
        {
            Stopwatch watch = Stopwatch.StartNew();
            ChatSnapshot snapshot = new ChatSnapshot();
            try
            {
                Target target = FindTarget("deepseek", false);
                snapshot.Title = target.Document.Current.Name ?? "DeepSeek Harness";
                snapshot.ConversationKey = target.Key;
                snapshot.Draft = ReadDraft(target.Composer);
                ReadLatestDeepSeekReply(target.Document, snapshot);
                snapshot.Connected = true;
                snapshot.Status = "已连接 DeepSeek Harness 当前对话";
            }
            catch (InvalidOperationException ex) { snapshot.Status = ex.Message; }
            catch (ElementNotAvailableException) { snapshot.Status = "DeepSeek 界面正在切换，稍后重试。"; }
            catch (COMException) { snapshot.Status = "DeepSeek 暂未提供可读取的界面文字。"; }
            catch (UnauthorizedAccessException) { snapshot.Status = "Windows 拒绝读取 DeepSeek，请使用相同权限运行。"; }
            catch (Exception) { snapshot.Status = "暂时无法读取 DeepSeek 当前对话。"; }
            finally { snapshot.ReadMs = watch.ElapsedMilliseconds; }
            return snapshot;
        }

        public static ClaudePlacement ReadPlacement(string profile, double callerScale)
        {
            ClaudePlacement result = new ClaudePlacement();
            if (callerScale <= 0 || Double.IsNaN(callerScale) || Double.IsInfinity(callerScale)) return result;
            try
            {
                Target target = FindTarget(NormalizeProfile(profile), true);
                if (IsIconic(target.Window) || !IsWindowVisible(target.Window)) return result;
                Rect editor = target.Composer.Current.BoundingRectangle;
                NativeRect native;
                if (editor.IsEmpty || editor.Width <= 0 || editor.Height <= 0 || !GetWindowRect(target.Window, out native)) return result;
                if (native.Right <= native.Left || native.Bottom <= native.Top) return result;
                if (editor.Left < native.Left - 4 || editor.Top < native.Top - 4 || editor.Right > native.Right + 4 || editor.Bottom > native.Bottom + 4) return result;
                result.InputBounds = ToDip(editor, callerScale);
                result.WindowBounds = ToDip(new Rect(native.Left, native.Top, native.Right-native.Left, native.Bottom-native.Top), callerScale);
                MonitorInfo monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                IntPtr monitorHandle = MonitorFromWindow(target.Window, 2);
                if (monitorHandle != IntPtr.Zero && GetMonitorInfo(monitorHandle, ref monitor)
                    && monitor.Work.Right > monitor.Work.Left && monitor.Work.Bottom > monitor.Work.Top)
                    result.WorkArea = ToDip(new Rect(monitor.Work.Left, monitor.Work.Top, monitor.Work.Right-monitor.Work.Left, monitor.Work.Bottom-monitor.Work.Top), callerScale);
                IntPtr foreground = GetForegroundWindow();
                uint foregroundProcess;
                GetWindowThreadProcessId(foreground, out foregroundProcess);
                using (Process self = Process.GetCurrentProcess())
                    result.TargetForeground = foreground == target.Window || foregroundProcess == (uint)self.Id;
                result.Available = true;
            }
            catch (Exception) { }
            return result;
        }

        public static void FillJapanese(string profile, string expectedKey, string japanese)
        {
            string normalized = NormalizeProfile(profile);
            string display = Display(normalized);
            if (String.IsNullOrWhiteSpace(expectedKey) || !expectedKey.StartsWith(normalized+"|", StringComparison.Ordinal))
                throw new InvalidOperationException("尚未确认目标会话，请先连接当前 " + display + " 对话。");
            if (String.IsNullOrWhiteSpace(japanese)) throw new InvalidOperationException("译文为空，请先完成翻译。");
            if (japanese.Length > 5000) throw new InvalidOperationException("译文超过 5000 个字符，请缩短后再填入。");
            string desired = japanese.Trim();
            lock (fillLock)
            {
                try
                {
                    Target target = FindTarget(normalized, true);
                    RequireKey(target, expectedKey);
                    if (!target.Composer.Current.IsEnabled) throw new InvalidOperationException(display + " 输入框当前不可编辑。");
                    string initial = ReadDraft(target.Composer);
                    RejectConflict(initial, desired, display);
                    if (initial == desired) return;
                    EnsureSameComposer(target, expectedKey);
                    RejectConflict(ReadDraft(target.Composer), desired, display);
                    object supported;
                    if (target.Composer.TryGetCurrentPattern(ValuePattern.Pattern, out supported))
                    {
                        ValuePattern value = (ValuePattern)supported;
                        if (value.Current.IsReadOnly) throw new InvalidOperationException(display + " 输入框当前只读，请等待后重试。");
                        try { value.SetValue(desired); }
                        catch (InvalidOperationException) { }
                        catch (ArgumentException) { }
                    }
                    if (WaitForDraft(target, expectedKey, desired)) return;
                    string afterValue = ReadDraft(target.Composer);
                    // A partial/different draft is never overwritten or appended to.
                    if (afterValue.Length != 0)
                        throw new InvalidOperationException("填入后的文字与译文不一致，已停止；请检查 " + display + " 草稿，未发送消息。");
                    if (normalized != "deepseek")
                        throw new InvalidOperationException("ChatGPT 当前未接受安全填入；本版本不使用键盘回退。请自行复制译文，未发送消息。");
                    TypeDeepSeekIntoEmptyComposer(target, expectedKey, desired);
                    if (!WaitForDraft(target, expectedKey, desired))
                        throw new InvalidOperationException("DeepSeek 填入后未能精确读回，请检查当前草稿，未发送消息。");
                }
                catch (ElementNotAvailableException) { throw new InvalidOperationException(display + " 窗口或会话已变化，已停止填入，未发送消息。"); }
                catch (ElementNotEnabledException) { throw new InvalidOperationException(display + " 输入框当前不可编辑，请等待后重试。"); }
                catch (COMException) { throw new InvalidOperationException(display + " 暂未提供可靠的输入接口，未能确认填入，未发送消息。"); }
                catch (UnauthorizedAccessException) { throw new InvalidOperationException("Windows 拒绝写入 " + display + "，请使用相同权限运行。"); }
                catch (Win32Exception) { throw new InvalidOperationException("Windows 未接受安全键入，请检查目标窗口，未发送消息。"); }
            }
        }

        private static void TypeDeepSeekIntoEmptyComposer(Target target, string expectedKey, string desired)
        {
            // No virtual keys, Enter, tab, clipboard or paste API is used. Multiline
            // drafts require ValuePattern success; fallback accepts printable text only.
            foreach (char character in desired)
                if (Char.IsControl(character)) throw new InvalidOperationException("DeepSeek 的安全键入回退仅支持单行译文；请去掉换行或控制字符后重试。");
            EnsureSameComposer(target, expectedKey);
            if (ReadDraft(target.Composer).Length != 0) throw new InvalidOperationException("DeepSeek 已有草稿，已停止键入以避免覆盖。");
            SetForegroundWindow(target.Window);
            Thread.Sleep(35);
            RequireForeground(target);
            target.Composer.SetFocus();
            Thread.Sleep(25);
            RequireFocusedComposer(target);
            EnsureSameComposer(target, expectedKey);
            if (ReadDraft(target.Composer).Length != 0) throw new InvalidOperationException("DeepSeek 草稿已变化，已停止键入。");
            int offset = 0;
            while (offset < desired.Length)
            {
                RequireFocusedComposer(target);
                EnsureSameComposer(target, expectedKey);
                string prefix = desired.Substring(0, offset);
                if (ReadDraft(target.Composer) != prefix) throw new InvalidOperationException("DeepSeek 草稿在键入中发生变化，已停止，未发送消息。");
                int length = Math.Min(24, desired.Length-offset);
                if (offset+length < desired.Length && Char.IsHighSurrogate(desired[offset+length-1])) length--;
                NativeInput[] inputs = new NativeInput[length*2];
                for (int i=0; i<length; i++)
                {
                    inputs[i*2].Type=1; inputs[i*2].Data.Keyboard.Scan=desired[offset+i]; inputs[i*2].Data.Keyboard.Flags=4;
                    inputs[i*2+1].Type=1; inputs[i*2+1].Data.Keyboard.Scan=desired[offset+i]; inputs[i*2+1].Data.Keyboard.Flags=6;
                }
                RequireForeground(target);
                uint inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeInput)));
                RequireForeground(target);
                if (inserted != inputs.Length) throw new InvalidOperationException("Windows 只接受了部分日文键入，已停止，请检查草稿，未发送消息。");
                offset += length;
                if (!WaitForDraft(target, expectedKey, desired.Substring(0,offset)))
                    throw new InvalidOperationException("DeepSeek 键入后的文字无法精确确认，已停止，未发送消息。");
            }
            RequireFocusedComposer(target);
        }

        private static void RequireForeground(Target target)
        {
            if (GetForegroundWindow() != target.Window) throw new InvalidOperationException("前台窗口已变化，已停止键入，未发送消息。");
        }
        private static void RequireFocusedComposer(Target target)
        {
            RequireForeground(target);
            AutomationElement focus = AutomationElement.FocusedElement;
            for (int i=0; focus!=null && i<8; i++, focus=TreeWalker.RawViewWalker.GetParent(focus))
                if (Automation.Compare(focus,target.Composer)) return;
            throw new InvalidOperationException("焦点不在 DeepSeek 主输入框，已停止键入，未发送消息。");
        }
        private static void RejectConflict(string current, string desired, string display)
        {
            if (current.Length != 0 && current != desired)
                throw new InvalidOperationException(display + " 已有其他草稿，请先清空后再填入；不会覆盖现有文字。");
        }
        private static bool WaitForDraft(Target target, string expectedKey, string expected)
        {
            for (int attempt=0; attempt<5; attempt++)
            {
                EnsureSameComposer(target,expectedKey);
                if (ReadDraft(target.Composer) == expected) return true;
                if (attempt<4) Thread.Sleep(35);
            }
            return false;
        }
        private static void EnsureSameComposer(Target target, string expectedKey)
        {
            RequireKey(target,expectedKey);
            AutomationElement current = FindComposer(target.Document,target.Profile,true);
            if (!Automation.Compare(current,target.Composer)) throw new InvalidOperationException("聊天输入框已切换，请重新确认会话后重试。");
        }
        private static void RequireKey(Target target, string expectedKey)
        {
            string actual = BuildKey(target.Profile,target.Window,target.Document,target.Composer);
            if (actual != expectedKey) throw new InvalidOperationException("目标会话已切换，请重新连接当前会话后再填入。");
        }

        private static Target FindTarget(string profile, bool requireVisibleComposer)
        {
            profile=NormalizeProfile(profile);
            HashSet<string> processNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SupportedApp app=AppCatalog.Find(profile);
            if (app!=null) foreach(string processName in app.ProcessNames) processNames.Add(processName);
            if (profile=="deepseek") { processNames.Add("DeepSeek Harness"); processNames.Add("deepseek"); }
            else processNames.Add("ChatGPT");
            HashSet<IntPtr> windows=new HashSet<IntPtr>();
            foreach(string processName in processNames)
            foreach(Process process in Process.GetProcessesByName(processName))
            {
                try { IntPtr hwnd=process.MainWindowHandle; if(hwnd!=IntPtr.Zero&&IsWindow(hwnd)&&IsWindowVisible(hwnd))windows.Add(hwnd); }
                catch(InvalidOperationException){} catch(Win32Exception){} finally{process.Dispose();}
            }
            if(windows.Count!=1)throw new InvalidOperationException(windows.Count==0?"请先打开 "+Display(profile)+" 客户端。":"发现多个 "+Display(profile)+" 窗口，请保留一个后重试。");
            IntPtr selected=IntPtr.Zero;foreach(IntPtr hwnd in windows)selected=hwnd;
            AutomationElement root=AutomationElement.FromHandle(selected),document=null;
            int documents=0;
            foreach(AutomationElement candidate in root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document)))
            {
                Uri url;
                if(!TryDocumentUrl(candidate,out url)||!AllowedDocument(profile,url))continue;
                document=candidate;documents++;
            }
            if(documents!=1)throw new InvalidOperationException("未找到唯一的 "+Display(profile)+" 聊天页面，请关闭其他页面或弹窗后重试。");
            AutomationElement composer=FindComposer(document,profile,requireVisibleComposer);
            return new Target{Profile=profile,Window=selected,Root=root,Document=document,Composer=composer,Key=BuildKey(profile,selected,document,composer)};
        }
        private static AutomationElement FindComposer(AutomationElement document,string profile,bool requireVisible)
        {
            List<AutomationElement> all=new List<AutomationElement>(),visible=new List<AutomationElement>();
            foreach(AutomationElement edit in document.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit)))
            {
                string name=edit.Current.Name??"",classes=edit.Current.ClassName??"";
                if(name.IndexOf("搜索",StringComparison.Ordinal)>=0||name.IndexOf("Search",StringComparison.OrdinalIgnoreCase)>=0)continue;
                bool match=profile=="deepseek"?(name.StartsWith("发消息或创建任务",StringComparison.Ordinal)||name.StartsWith("描述你想要构建的内容",StringComparison.Ordinal)):HasClassToken(classes,"ProseMirror");
                if(!match)continue;all.Add(edit);if(!edit.Current.IsOffscreen)visible.Add(edit);
            }
            if(visible.Count==1)return visible[0];
            if(!requireVisible&&visible.Count==0&&all.Count==1)return all[0];
            throw new InvalidOperationException("未找到唯一"+(requireVisible?"可见":"")+"的 "+Display(profile)+" 主输入框；搜索框不会作为目标。");
        }
        private static string BuildKey(string profile,IntPtr hwnd,AutomationElement document,AutomationElement composer)
        {
            string identity;
            if(profile=="deepseek")
            {
                // A newly opened compose page can retain the previous sidebar
                // selection briefly. Its dedicated new-chat composer takes
                // precedence, so an old session key can never authorize this page.
                if((composer.Current.Name??"").StartsWith("描述你想要构建的内容",StringComparison.Ordinal))
                    return profile+"|"+hwnd.ToInt64()+"|new:"+Runtime(composer);
                AutomationElement selected=null;int selections=0;
                foreach(AutomationElement row in document.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TreeItem)))
                {
                    if(!HasClassSuffix(row.Current.ClassName,"_sessionRow"))continue;
                    object pattern;if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern)&&((SelectionItemPattern)pattern).Current.IsSelected){selected=row;selections++;}
                }
                if(selections==1)identity="session:"+Runtime(selected);
                else throw new InvalidOperationException("无法唯一确认 DeepSeek 当前会话，请展开会话侧边栏并选择一个会话后重试。");
            }
            else
            {
                Uri url;if(!TryDocumentUrl(document,out url)||!AllowedDocument(profile,url))throw new InvalidOperationException("ChatGPT 未提供可确认的会话地址，暂不填入。");
                identity=url.GetLeftPart(UriPartial.Path);
            }
            return profile+"|"+hwnd.ToInt64()+"|"+identity;
        }
        private static void ReadLatestDeepSeekReply(AutomationElement document,ChatSnapshot snapshot)
        {
            AutomationElement marker=null;
            foreach(AutomationElement button in document.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)))
                if((button.Current.Name??"")=="好的回答")marker=button;
            if(marker==null)return;
            // Feedback buttons are assistant-only evidence. The immediately preceding
            // copy button and content group distinguish it from the user's _bubble.
            TreeWalker raw=TreeWalker.RawViewWalker;
            AutomationElement copy=raw.GetPreviousSibling(marker);
            if(copy==null||copy.Current.ControlType!=ControlType.Button||(copy.Current.Name??"")!="复制")return;
            AutomationElement content=raw.GetPreviousSibling(copy);
            if(content==null||content.Current.ControlType!=ControlType.Group||HasClassSuffix(content.Current.ClassName,"_bubble"))return;
            StringBuilder text=new StringBuilder();AppendContentText(content,text,0);
            snapshot.Reply=text.ToString().Trim();
            if(snapshot.Reply.Length>0)snapshot.ReplyId=Runtime(content);
        }
        private static void AppendContentText(AutomationElement node,StringBuilder output,int depth)
        {
            if(depth>24||output.Length>=MaxText)return;
            ControlType type=node.Current.ControlType;
            if(type==ControlType.Button||type==ControlType.Edit||type==ControlType.StatusBar||type==ControlType.Menu)return;
            if(type==ControlType.Text)
            {
                string value=node.Current.Name??"";if(value.Length>0){if(output.Length>0)output.AppendLine();output.Append(value,0,Math.Min(value.Length,MaxText-output.Length));}return;
            }
            TreeWalker raw=TreeWalker.RawViewWalker;AutomationElement child=raw.GetFirstChild(node);
            while(child!=null&&output.Length<MaxText){AppendContentText(child,output,depth+1);child=raw.GetNextSibling(child);}
        }
        private static string ReadDraft(AutomationElement composer)
        {
            object supported;
            if(composer.TryGetCurrentPattern(ValuePattern.Pattern,out supported))return ((ValuePattern)supported).Current.Value??"";
            if(composer.TryGetCurrentPattern(TextPattern.Pattern,out supported))
            {
                string text=((TextPattern)supported).DocumentRange.GetText(MaxText)??"",name=composer.Current.Name??"";
                if(text.Trim()==name&&(name.StartsWith("发消息或创建任务",StringComparison.Ordinal)||name.StartsWith("描述你想要构建的内容",StringComparison.Ordinal)))return "";
                return text;
            }
            throw new InvalidOperationException("当前输入框不提供可核验的草稿文字，暂不填入。");
        }
        private static string Runtime(AutomationElement element){return String.Join(".",Array.ConvertAll(element.GetRuntimeId(),value=>value.ToString()));}
        private static bool HasClassToken(string classes,string expected){foreach(string token in (classes??"").Split((char[])null,StringSplitOptions.RemoveEmptyEntries))if(token==expected)return true;return false;}
        private static bool HasClassSuffix(string classes,string suffix){foreach(string token in (classes??"").Split((char[])null,StringSplitOptions.RemoveEmptyEntries))if(token.EndsWith(suffix,StringComparison.Ordinal))return true;return false;}
        private static bool TryDocumentUrl(AutomationElement document,out Uri url){url=null;object pattern;return document.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)&&Uri.TryCreate(((ValuePattern)pattern).Current.Value,UriKind.Absolute,out url);}
        private static bool AllowedDocument(string profile,Uri url){return profile=="deepseek"?url.Scheme=="dsh-app"&&url.Host=="app"&&url.AbsolutePath=="/":url.Scheme=="https"&&(url.Host=="chatgpt.com"||url.Host=="chat.openai.com");}
        private static string NormalizeProfile(string profile){if(String.Equals(profile,"deepseek",StringComparison.OrdinalIgnoreCase))return "deepseek";if(String.Equals(profile,"chatgpt",StringComparison.OrdinalIgnoreCase))return "chatgpt";throw new InvalidOperationException("此适配器仅支持 ChatGPT 客户端和 DeepSeek Harness。");}
        private static string Display(string profile){return profile=="deepseek"?"DeepSeek Harness":"ChatGPT";}
        private static Rect ToDip(Rect value,double scale){return new Rect(value.Left/scale,value.Top/scale,value.Width/scale,value.Height/scale);}
    }
}
