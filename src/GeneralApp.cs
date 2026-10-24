using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yuqiao
{
    public static class GeneralEntry
    {
        [STAThread] public static void Main()
        {
            using (var instance = new Mutex(false, AppRuntime.MutexName))
            {
                bool acquired;
                try { acquired = instance.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    try { using (var wake = EventWaitHandle.OpenExisting(AppRuntime.MutexName + "_Show")) wake.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return;
                }
                try
                {
                    var app = new Application();
                    var window = new GeneralWindow();
                    using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, AppRuntime.MutexName + "_Show"))
                    {
                        var registration = ThreadPool.RegisterWaitForSingleObject(wake, (s, t) =>
                        {
                            if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(new Action(window.RestoreForUser));
                        }, null, Timeout.Infinite, false);
                        try { app.Run(window); } finally { registration.Unregister(null); }
                    }
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    /// <summary>Independent translation only: never reads or fills another application.</summary>
    public sealed class GeneralWindow : Window
    {
        readonly TranslationService service;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        readonly TextBox source, first, second;
        readonly TextBlock firstLabel, secondLabel, status;
        readonly ComboBox direction;
        readonly CheckBox live;
        readonly Button copyFirst, copySecond;
        CancellationTokenSource cancellation;
        int revision;
        bool closed, composing;
        internal Func<string, string, string, CancellationToken, Task<TranslationResult>> translator;

        static readonly Brush Surface = new SolidColorBrush(Color.FromRgb(24, 27, 35));
        static readonly Brush Card = new SolidColorBrush(Color.FromRgb(35, 40, 51));
        static readonly Brush TextColor = new SolidColorBrush(Color.FromRgb(232, 236, 243));
        static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(163, 177, 197));
        static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(126, 211, 195));

        public GeneralWindow()
        {
            service = new TranslationService();
            translator = service.TranslateAsync;
            Title = "语桥 · 通用即时翻译";
            Width = 580; Height = Math.Min(760, SystemParameters.WorkArea.Height - 40);
            MinWidth = 430; MinHeight = Math.Min(540, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            Background = Surface; Foreground = TextColor;
            var outer = new Grid { Margin = new Thickness(22) }; Content = outer;
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; outer.Children.Add(header);
            header.Children.Add(new TextBlock { Text = "语桥  /  Yuqiao", FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = Accent });
            header.Children.Add(new TextBlock { Text = "写下想说的话，翻译即刻跟上。", Foreground = Muted, Margin = new Thickness(0, 5, 0, 12) });
            direction = new ComboBox { SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(8, 5, 8, 5) };
            direction.Items.Add("中文 → 日文 + 英文"); direction.Items.Add("日文 → 中文"); direction.Items.Add("英文 → 中文");
            AutomationProperties.SetName(direction, "翻译方向"); header.Children.Add(direction);
            var controls = new WrapPanel(); header.Children.Add(controls);
            live = new CheckBox { Content = "自动翻译", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Foreground = TextColor };
            controls.Children.Add(live);
            var pin = new CheckBox { Content = "置顶", Foreground = TextColor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            pin.Checked += (s, e) => Topmost = true; pin.Unchecked += (s, e) => Topmost = false; controls.Children.Add(pin);
            var voice = Button("语音输入", () =>
            {
                if (direction.SelectedIndex != 0) { status.Text = "中文听写请先选择「中文 → 日文 + 英文」。"; return; }
                try { WindowsVoiceInput.Request(source); status.Text = "请说中文；结束听写请使用 Windows 系统面板。"; }
                catch (Exception) { status.Text = "系统听写未能打开。请选择中文输入法，再点一次语音输入。"; }
            });
            controls.Children.Add(voice);
            controls.Children.Add(Button("清空", () => { source.Clear(); source.Focus(); }));
            controls.Children.Add(Button("重试翻译", () => { Invalidate(); StartNow(); }));
            TextBlock sourceLabel; Button unused;
            var sourcePanel = Pane("输入原文", false, out source, out sourceLabel, out unused);
            Grid.SetRow(sourcePanel, 1); outer.Children.Add(sourcePanel); source.MaxLength = 5000;
            var firstPanel = Pane("日文", true, out first, out firstLabel, out copyFirst);
            Grid.SetRow(firstPanel, 2); outer.Children.Add(firstPanel);
            var secondPanel = Pane("英文", true, out second, out secondLabel, out copySecond);
            Grid.SetRow(secondPanel, 3); outer.Children.Add(secondPanel);
            status = new TextBlock { Text = "停顿约 0.6 秒后自动翻译 · 需要联网 · 无需大模型密钥", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(status, 4); outer.Children.Add(status);
            source.TextChanged += (s, e) => Invalidate();
            direction.SelectionChanged += (s, e) =>
            {
                bool chinese = direction.SelectedIndex == 0;
                firstLabel.Text = chinese ? "日文" : "中文";
                AutomationProperties.SetName(first, firstLabel.Text);
                secondPanel.Visibility = chinese ? Visibility.Visible : Visibility.Collapsed;
                outer.RowDefinitions[3].Height = chinese ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Invalidate();
            };
            live.Checked += (s, e) => Invalidate(); live.Unchecked += (s, e) => Invalidate();
            TextCompositionManager.AddPreviewTextInputStartHandler(source, (s, e) => { composing = true; timer.Stop(); });
            TextCompositionManager.AddPreviewTextInputHandler(source, (s, e) =>
            {
                composing = false;
                Dispatcher.BeginInvoke(new Action(() => { if (!closed) Invalidate(); }), DispatcherPriority.Background);
            });
            timer.Tick += (s, e) => StartNow();
            Closed += (s, e) => { closed = true; timer.Stop(); Cancel(); service.Dispose(); };
            Loaded += (s, e) => source.Focus();
        }

        static Button Button(string caption, Action action)
        {
            var button = new Button { Content = caption, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 4), Background = Card, Foreground = TextColor };
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'><Border Background='{TemplateBinding Background}' CornerRadius='4' BorderThickness='1' BorderBrush='#526172' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#364455'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            button.Click += (s, e) => action(); return button;
        }
        static Grid Pane(string caption, bool readOnly, out TextBox text, out TextBlock label, out Button copy)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
            var heading = new DockPanel(); grid.Children.Add(heading);
            var box = new TextBox { IsReadOnly = readOnly, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Card, Foreground = TextColor, CaretBrush = Accent, BorderThickness = new Thickness(0), Padding = new Thickness(12), FontSize = 15 };
            AutomationProperties.SetName(box, caption);
            Grid.SetRow(box, 1); grid.Children.Add(box); text = box;
            copy = Button("复制", () =>
            {
                try { if (!String.IsNullOrEmpty(box.Text)) Clipboard.SetText(box.Text); }
                catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show("剪贴板正忙，请稍后再试。", "语桥"); }
            });
            copy.IsEnabled = false; copy.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed; DockPanel.SetDock(copy, Dock.Right); heading.Children.Add(copy);
            label = new TextBlock { Text = caption, Foreground = Muted, Margin = new Thickness(0, 0, 0, 7), VerticalAlignment = VerticalAlignment.Center };
            heading.Children.Add(label); return grid;
        }
        void Cancel()
        {
            if (cancellation != null) { cancellation.Cancel(); cancellation.Dispose(); cancellation = null; }
        }
        void Invalidate()
        {
            revision++; Cancel(); timer.Stop();
            first.Clear(); second.Clear(); copyFirst.IsEnabled = copySecond.IsEnabled = false;
            if (closed) return;
            status.Text = live.IsChecked == true ? "正在等待输入停顿…" : "自动翻译已暂停";
            if (live.IsChecked == true && !composing && !String.IsNullOrWhiteSpace(source.Text)) timer.Start();
        }
        async void StartNow()
        {
            timer.Stop();
            if (closed || composing || String.IsNullOrWhiteSpace(source.Text)) return;
            Cancel(); cancellation = new CancellationTokenSource();
            var token = cancellation.Token; int version = ++revision;
            string text = source.Text, language = direction.SelectedIndex == 0 ? "zh-Hans" : direction.SelectedIndex == 1 ? "ja" : "en";
            bool chinese = direction.SelectedIndex == 0;
            status.Text = "正在翻译…";
            var one = Render(text, language, chinese ? "ja" : "zh-Hans", first, copyFirst, token, version);
            var two = chinese ? Render(text, language, "en", second, copySecond, token, version) : Task.FromResult(true);
            bool[] results = await Task.WhenAll(one, two);
            if (closed || version != revision || token.IsCancellationRequested) return;
            status.Text = results[0] && results[1] ? "翻译完成 · 可直接修改原文 · Enter 换行" : "部分翻译未完成，请检查网络或点击「重试翻译」。";
        }
        async Task<bool> Render(string text, string language, string target, TextBox output, Button copy, CancellationToken token, int version)
        {
            try
            {
                var result = await translator(text, language, target, token);
                if (closed || version != revision || token.IsCancellationRequested) return false;
                output.Text = result.Text; copy.IsEnabled = !String.IsNullOrWhiteSpace(result.Text);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception)
            {
                if (!closed && version == revision && !token.IsCancellationRequested) output.Text = "暂时无法翻译，请重试。";
                return false;
            }
        }
        public void RestoreForUser()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show(); Activate(); source.Focus();
        }
    }
}
