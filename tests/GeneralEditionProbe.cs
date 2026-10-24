using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Yuqiao;

class GeneralEditionProbe
{
    static int count;
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object value, string field) { return value.GetType().GetField(field, Flags).GetValue(value); }
    static void Set(object value, string field, object data) { value.GetType().GetField(field, Flags).SetValue(value, data); }
    static void Call(object value, string method) { value.GetType().GetMethod(method, Flags).Invoke(value, null); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
    static string Hash(string file) { using (var h = SHA256.Create()) using (var f = File.OpenRead(file)) return BitConverter.ToString(h.ComputeHash(f)); }
    static TranslationResult Result(string text) { return new TranslationResult { Text = text, Provider = "TEST" }; }
    static void Pump(int milliseconds)
    {
        var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(3);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        string path = Path.GetFullPath(args[0]), name = AssemblyName.GetAssemblyName(path).Name;
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => new AssemblyName(e.Name).Name == name ? Assembly.LoadFrom(path) : null;
        Assembly.LoadFrom(path);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        return Run(path);
    }
    static int Run(string path)
    {
        GeneralWindow window = null;
        try
        {
            var assembly = typeof(GeneralWindow).Assembly;
            Check(Hash(assembly.Location) == Hash(path), "exact general release binary is tested");
            Check(assembly.GetType("Yuqiao.BridgeWindow") == null && assembly.GetType("Yuqiao.ChatWindowReader") == null && assembly.GetType("Yuqiao.ClaudeComposer") == null, "general binary contains no chat reader or auto-fill implementation");
            Check(assembly.GetType("Yuqiao.ClaudeBinding") == null && assembly.GetType("Yuqiao.BindingInstaller") == null, "general binary contains no auto-launch installer");
            window = new GeneralWindow();
            var source = (TextBox)Get(window, "source"); var first = (TextBox)Get(window, "first"); var second = (TextBox)Get(window, "second");
            var direction = (ComboBox)Get(window, "direction"); var live = (CheckBox)Get(window, "live");
            var copyFirst = (Button)Get(window, "copyFirst"); var copySecond = (Button)Get(window, "copySecond");
            int requests = 0;
            Func<string, string, string, CancellationToken, Task<TranslationResult>> success = (text, from, to, token) =>
            { requests++; return Task.FromResult(Result(from + ">" + to + ":" + text)); };
            Set(window, "translator", success);
            Check(first.IsReadOnly && second.IsReadOnly && source.MaxLength == 5000, "results are read-only and input is limited to 5000 characters");
            source.Text = "你好。"; Call(window, "StartNow");
            Check(first.Text == "zh-Hans>ja:你好。" && second.Text == "zh-Hans>en:你好。", "Chinese produces Japanese and English with explicit language pairs");
            Check(copyFirst.IsEnabled && copySecond.IsEnabled, "copy enables only after fresh success");
            direction.SelectedIndex = 1; source.Text = "こんにちは。"; Call(window, "StartNow");
            Check(first.Text == "ja>zh-Hans:こんにちは。" && second.Text == "", "Japanese produces Chinese without stale English");
            Check(((Grid)second.Parent).Visibility == Visibility.Collapsed, "unused result panel collapses in reverse mode");
            direction.SelectedIndex = 2; source.Text = "Hello."; Call(window, "StartNow");
            Check(first.Text == "en>zh-Hans:Hello.", "English can be translated back into Chinese");
            live.IsChecked = false; source.Text = "暂停"; int before = requests; Pump(700);
            Check(requests == before && first.Text == "", "pausing prevents automatic requests and clears outdated results");
            live.IsChecked = true; Pump(750);
            Check(requests > before && first.Text.EndsWith("暂停"), "automatic translation resumes after the debounce");
            direction.SelectedIndex = 1;
            var pending = new TaskCompletionSource<TranslationResult>();
            Set(window, "translator", new Func<string, string, string, CancellationToken, Task<TranslationResult>>((a, b, c, d) => pending.Task));
            source.Text = "古い"; Call(window, "StartNow");
            source.Text = "新しい"; Set(window, "translator", success); Call(window, "StartNow");
            pending.SetResult(Result("STALE")); Pump(30);
            Check(first.Text == "ja>zh-Hans:新しい", "late old results cannot replace newer text even if a provider ignores cancellation");
            pending = new TaskCompletionSource<TranslationResult>();
            Set(window, "translator", new Func<string, string, string, CancellationToken, Task<TranslationResult>>((a, b, c, d) => pending.Task));
            source.Text = "止める"; Call(window, "StartNow"); live.IsChecked = false;
            pending.SetResult(Result("STALE")); Pump(30);
            Check(first.Text == "" && !copyFirst.IsEnabled, "pausing invalidates an in-flight result");
            direction.SelectedIndex = 0; live.IsChecked = true;
            Set(window, "translator", new Func<string, string, string, CancellationToken, Task<TranslationResult>>((a, b, c, d) =>
            {
                if (c == "ja") return Task.FromResult(Result("日本語。"));
                var failed = new TaskCompletionSource<TranslationResult>(); failed.SetException(new InvalidOperationException()); return failed.Task;
            }));
            source.Text = "测试独立失败"; Call(window, "StartNow");
            Check(first.Text == "日本語。" && copyFirst.IsEnabled, "English failure preserves successful Japanese");
            Check(!copySecond.IsEnabled && second.Text.Contains("重试"), "failed translation cannot be copied as a valid result");
            Set(window, "translator", success); Call(window, "Invalidate"); Call(window, "StartNow");
            Check(copySecond.IsEnabled && second.Text.Contains("zh-Hans>en"), "manual retry recovers a failed lane");
            source.Clear();
            Check(first.Text == "" && second.Text == "" && !copyFirst.IsEnabled && !copySecond.IsEnabled, "clear removes both results and disables copy");
            before = requests; Call(window, "StartNow");
            Check(requests == before, "empty input creates no request");
            Set(window, "composing", true); source.Text = "输入法候选"; Call(window, "StartNow");
            Check(requests == before, "unfinished IME composition is never translated");
            Set(window, "composing", false); Call(window, "Invalidate"); Pump(750);
            Check(requests > before && first.Text.Contains("输入法候选"), "completed composition resumes translation");
            window.Close(); window = null;
            Check(true, "all checks used synthetic text with no microphone, clipboard change, network, or chat interaction");
            Console.WriteLine("ALL " + count + " CHECKS PASSED"); return 0;
        }
        catch (Exception error) { Console.WriteLine(error.ToString()); return 1; }
        finally { if (window != null) window.Close(); }
    }
}
