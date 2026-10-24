// Isolated QA: constructs only its own invisible WPF Window and an in-memory HTTP handler.
// It never calls ChatWindowReader.Read, Window.Show, input APIs, or real network services.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace YuqiaoQa
{
    internal sealed class FakeHandler : HttpMessageHandler
    {
        public bool Fail;
        public int Requests, Completed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            await Task.Delay(160).ConfigureAwait(false);
            Interlocked.Increment(ref Completed);
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("fixture failure") };
            string text = request.RequestUri.Query.Contains("to=ja") ? "テスト訳文" : "Fixture translation";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"translations\":[{\"text\":\"" + text + "\"}]}]", Encoding.UTF8, "application/json")
            };
        }
    }

    internal static class UiStateProbe
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly List<string> Results = new List<string>();
        private static int failures;
        private static object Get(object value, string name) { return value.GetType().GetField(name, Fields).GetValue(value); }
        private static void Set(object value, string name, object fieldValue) { value.GetType().GetField(name, Fields).SetValue(value, fieldValue); }
        private static object Call(object value, string method, params object[] args)
        {
            try { return value.GetType().GetMethod(method, Fields).Invoke(value, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
        private static TextBox Box(object lane, string name) { return (TextBox)Get(lane, name); }
        private static void Check(string name, bool passed, string detail)
        {
            Results.Add((passed ? "PASS" : "FAIL") + "\t" + name + "\t" + detail);
            if (!passed) failures++;
        }
        private static void Pump(Task task)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 5000)
            {
                DispatcherFrame frame = new DispatcherFrame();
                DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
                timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            if (!task.IsCompleted) throw new TimeoutException("Fixture task exceeded 5 seconds.");
            task.GetAwaiter().GetResult();
        }
        private static Task Translate(object window, object lane, string source)
        {
            return (Task)Call(window, "RunTranslation", lane, true, source, (int)Get(lane, "Version"), (int)Get(window, "epoch"), DateTime.UtcNow, CancellationToken.None);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            string assemblyPath = Path.GetFullPath(args[0]);
            string resultPath = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
            string hash;
            using (SHA256 sha = SHA256.Create()) using (FileStream input = File.OpenRead(assemblyPath))
                hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            Results.Add("Compiled-product SHA256\t" + hash);
            Results.Add("Scope\tOwn invisible BridgeWindow only; fake HTTP; no UIA reads; no real ChatGPT operation.");
            Window window = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Assembly product = Assembly.LoadFrom(assemblyPath);
                Type windowType = product.GetType("Yuqiao.BridgeWindow", true);
                string diagnostics = Path.Combine(Path.GetDirectoryName(resultPath), "ui-fixture-metrics.tsv");
                window = (Window)Activator.CreateInstance(windowType, new object[] { new[] { "--test-window", "0x1234", "--diagnostics", diagnostics } });
                object outgoing = Get(window, "outgoing"), incoming = Get(window, "incoming");
                Check("no window shown", !window.IsVisible && !(bool)Get(window, "busy"), "Loaded/timer never started");
                Check("combined CLI arguments", (IntPtr)Get(window, "testWindow") == new IntPtr(0x1234) && (string)Get(window, "diagnosticFile") == diagnostics, "hex handle and diagnostics parsed together");
                Check("Chinese composer is local editable input", !Box(outgoing,"Source").IsReadOnly && System.Windows.Automation.AutomationProperties.GetName(Box(outgoing,"Source")) == "中文输入", "local Chinese source is editable and explicitly named");
                Box(outgoing,"Source").Text="本地输入触发";
                Check("local input observes changes", (string)Get(outgoing,"Seen") == "本地输入触发", "TextChanged updates the outgoing lane without reader drafts");

                Call(window, "Observe", outgoing, "你好", false, "");
                Box(outgoing, "First").Text = "old English";
                Box(outgoing, "Second").Text = "旧日文";
                int version = (int)Get(outgoing, "Version");
                Call(window, "Observe", outgoing, "", false, "");
                Check("clear draft clears translations", Box(outgoing, "Source").Text == "" && Box(outgoing, "First").Text == "" && Box(outgoing, "Second").Text == "" && (int)Get(outgoing, "Version") > version, "source/EN/JA empty; version advanced");

                Call(window, "Observe", incoming, "こんにちは", true, "message-1");
                version = (int)Get(incoming, "Version");
                Call(window, "Observe", incoming, "こんにちは", true, "message-1");
                Check("same identity deduplicates", (int)Get(incoming, "Version") == version, "same ID and content do not restart");
                Box(incoming, "First").Text = "你好";
                Call(window, "Observe", incoming, "こんにちは", true, "message-2");
                Check("same content new message retained", (int)Get(incoming, "Version") > version && (string)Get(incoming, "Id") == "message-2" && Box(incoming, "First").Text == "", "new ID restarts and removes old result");

                Call(window, "Observe", outgoing, "暂停前草稿", false, "");
                Box(outgoing, "First").Text = "stale English";
                Box(outgoing, "Second").Text = "古い訳文";
                Box(incoming, "First").Text = "旧中文";
                int epoch = (int)Get(window, "epoch");
                Call(window, "Pause", "fixture paused");
                Check("pause keeps Chinese and clears old translations", Box(outgoing, "Source").Text == "暂停前草稿" && (string)Get(outgoing,"Seen") == "暂停前草稿" && Box(outgoing, "First").Text == "" && Box(outgoing, "Second").Text == "" && Box(incoming, "Source").Text == "" && Box(incoming, "First").Text == "", "local draft preserved; stale translations and remote source removed");
                Check("pause invalidates connection epoch", (int)Get(window, "epoch") > epoch, "prior generation no longer current");

                Call(window,"Observe",outgoing,"",false,"");((TextBlock)Get(outgoing,"Status")).Text="翻译已暂停，当前无有效译文。";
                Call(window,"RestoreWaitingStatus",outgoing,false);Call(window,"RestoreWaitingStatus",incoming,true);
                Check("recovered empty lanes show waiting", ((TextBlock)Get(outgoing,"Status")).Text == "在上方输入中文，停顿后自动翻译…" && ((TextBlock)Get(incoming,"Status")).Text == "等待当前日文回复…", "connected baseline removes stale paused labels");
                Box(outgoing,"First").Text="valid result";((TextBlock)Get(outgoing,"Status")).Text="valid status";
                Call(window,"RestoreWaitingStatus",outgoing,false);
                Check("recovery preserves valid result", Box(outgoing,"First").Text=="valid result" && ((TextBlock)Get(outgoing,"Status")).Text=="valid status", "existing displayed translation is untouched");

                CheckBox active = (CheckBox)Get(window, "active");
                Box(outgoing,"Source").Text="重连前的中文草稿";
                active.IsChecked = false;
                Call(window, "Reset");
                object reader = Get(window, "reader");
                Check("reset retains test target", (IntPtr)Get(window, "testWindow") == new IntPtr(0x1234) && (IntPtr)Get(reader, "testWindow") == new IntPtr(0x1234), "same explicit handle passed to replacement reader");
                Check("reset resumes automation", active.IsChecked == true && !(bool)Get(window, "baseline") && (string)Get(window, "boundTitle") == "", "checked and ready for a fresh baseline");
                Check("reset retains local Chinese", Box(outgoing,"Source").Text == "重连前的中文草稿" && (string)Get(outgoing,"Seen") == "重连前的中文草稿" && (string)Get(outgoing,"Done") == "", "reconnection reuses Chinese source and requires fresh translation");

                ComboBox target = (ComboBox)Get(window, "target");
                Check("Claude is default target", (string)Get(window,"profile") == "claude" && target.SelectedIndex == 0 && window.Title == "语桥实时翻译 · 桌面伴侣", "neutral window title; Claude selected");
                epoch = (int)Get(window,"epoch");
                target.SelectedIndex = 1;
                Check("target switch resets safely", (string)Get(window,"profile") == "chatgpt" && (int)Get(window,"epoch") > epoch && (IntPtr)Get(Get(window,"reader"),"testWindow") == new IntPtr(0x1234), "ChatGPT test selection resets; explicit test target has priority");
                target.SelectedIndex = 0;
                Check("Claude initial creation accepted", (bool)Call(window,"IsInitialClaudeTransition","claude|123|https://claude.ai/new","claude|123|https://claude.ai/chat/first-id"), "same HWND /new to /chat/id");
                Check("another chat not accepted", !(bool)Call(window,"IsInitialClaudeTransition","claude|123|https://claude.ai/chat/first-id","claude|123|https://claude.ai/chat/second-id"), "existing chat identity changes require reconnection");
                Check("another window not accepted", !(bool)Call(window,"IsInitialClaudeTransition","claude|123|https://claude.ai/new","claude|456|https://claude.ai/chat/first-id"), "HWND differs");
                Check("title fallback cannot spoof initial creation", !(bool)Call(window,"IsInitialClaudeTransition","title:new/new","title:new/chat/first-id"), "requires structured Claude profile/window/URL key");
                Set(window,"newConversationTransitionAvailable",false);
                Check("initial creation only once", !(bool)Call(window,"IsInitialClaudeTransition","claude|123|https://claude.ai/new","claude|123|https://claude.ai/chat/first-id"), "consumed transition is rejected");
                Set(window,"newConversationTransitionAvailable",true);

                IDisposable originalService = (IDisposable)Get(window, "service");
                Type serviceType = product.GetType("Yuqiao.TranslationService", true);
                FakeHandler handler = new FakeHandler();
                object fakeService = Activator.CreateInstance(serviceType, Fields, null, new object[] { handler }, null);
                Set(window, "service", fakeService);
                originalService.Dispose();

                Call(window, "Observe", outgoing, "明日会いましょう。", false, "");
                Set(outgoing,"Changed",DateTime.UtcNow.AddSeconds(-2));
                int requestsBeforeJapanese=handler.Requests;
                Pump((Task)Call(window,"Schedule",outgoing,true));
                Check("Japanese draft is not Chinese", ((TextBlock)Get(outgoing,"Status")).Text == "草稿已是日文，等待中文输入…" && handler.Requests == requestsBeforeJapanese, "kanji plus kana does not enter Chinese dual translation");

                Call(window, "Observe", outgoing, "较早草稿", false, "");
                Task stale = Translate(window, outgoing, "较早草稿");
                Call(window, "Observe", outgoing, "最新草稿", false, "");
                Box(outgoing, "First").Text = "NEW-EN"; Box(outgoing, "Second").Text = "NEW-JA";
                Pump(stale);
                Check("late draft result rejected", Box(outgoing, "First").Text == "NEW-EN" && Box(outgoing, "Second").Text == "NEW-JA", "delayed uncancelled request cannot overwrite a newer version");

                Call(window, "Observe", outgoing, "上个会话草稿", false, "");
                Task oldEpoch = Translate(window, outgoing, "上个会话草稿");
                Set(window, "epoch", (int)Get(window, "epoch") + 1);
                Box(outgoing, "First").Text = "CURRENT-EN"; Box(outgoing, "Second").Text = "CURRENT-JA";
                Pump(oldEpoch);
                Check("late connection result rejected", Box(outgoing, "First").Text == "CURRENT-EN" && Box(outgoing, "Second").Text == "CURRENT-JA", "epoch alone rejects completion despite unchanged lane version");

                Call(window, "Observe", outgoing, "当前有效草稿", false, "");
                Task current = Translate(window, outgoing, "当前有效草稿"); Pump(current);
                Check("current dual result accepted", Box(outgoing, "First").Text == "Fixture translation" && Box(outgoing, "Second").Text == "テスト訳文", "both fake translations applied on Dispatcher");

                Call(window,"Observe",outgoing,"填入前的中文",false,"");Set(outgoing,"Changed",DateTime.UtcNow.AddSeconds(-2));
                Set(window,"connected",true);Set(window,"boundConversationKey","claude|123|https://claude.ai/chat/fixture");
                Pump((Task)Call(window,"Schedule",outgoing,true));
                Check("fill disabled while translation pending", !(bool)Call(window,"CanFillJapanese"), "no stale or incomplete Japanese can be filled");
                Pump((Task)Get(outgoing,"Pending"));Call(window,"UpdateFillState");
                Check("fill accepts latest completed Japanese", (bool)Call(window,"CanFillJapanese") && ((Button)Get(window,"fillJapanese")).IsEnabled, "same current source/version; connected Claude; no writer invocation");
                Box(incoming,"First").Text="远端旧译文";Set(incoming,"Seen","远端旧文本");
                Call(window,"MarkDisconnected","fixture disconnected");
                Check("disconnect keeps local result and disables fill", Box(outgoing,"Source").Text=="填入前的中文" && Box(outgoing,"Second").Text=="テスト訳文" && !(bool)Call(window,"CanFillJapanese") && Box(incoming,"First").Text=="", "remote invalid state cleared; local draft/result protected");
                Set(window,"connected",true);Box(outgoing,"Source").Text="又改了一次中文";
                Check("changed Chinese disables fill immediately", !(bool)Call(window,"CanFillJapanese") && Box(outgoing,"Second").Text=="" && (string)Get(outgoing,"Seen")=="又改了一次中文", "new source invalidates prior Japanese before any network task");

                handler.Fail = true;
                int completed = handler.Completed;
                Call(window, "Observe", outgoing, "双请求失败样例", false, "");
                Task failed = Translate(window, outgoing, "双请求失败样例"); Pump(failed);
                string status = ((TextBlock)Get(outgoing, "Status")).Text;
                Check("dual failure observed", failed.Status == TaskStatus.RanToCompletion && handler.Completed - completed == 4 && (status.Contains("暂时不可用") || status.Contains("暂时无法连接")) && Box(outgoing, "First").Text == "" && Box(outgoing, "Second").Text == "", "completed=" + (handler.Completed-completed) + "; task=" + failed.Status + "; status=" + status + "; firstLength=" + Box(outgoing,"First").Text.Length + "; secondLength=" + Box(outgoing,"Second").Text.Length);
                Check("no real target read", !(bool)Get(window, "busy") && !window.IsVisible, "fixture remained invisible and timer never ran");
            }
            catch (Exception ex)
            {
                failures++;
                Results.Add("FAIL\tUnexpected exception\t" + ex);
            }
            finally
            {
                if (window != null) window.Close();
                Results.Add("RESULT\t" + (failures == 0 ? "ALL PASSED" : "FAILED") + "\tFailures=" + failures);
                File.WriteAllLines(resultPath, Results, Encoding.UTF8);
                foreach (string line in Results) Console.WriteLine(line);
            }
            return failures == 0 ? 0 : 1;
        }
    }
}
