using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Yuqiao
{
    // Views only: all text, translation requests, target following and fill actions
    // are supplied by the main application. These windows do not read or write Claude.
    public sealed class FloatingInputWindow : Window
    {
        public TextBox Input;
        public TextBlock Status;
        public ListBox Candidates;
        public Button Fill;
        public Button Voice;
        public CheckBox Follow;
        public CheckBox AutoFill;
        public Button FullMode;
        public ComboBox LanguageSelector;
        public TextBlock EmptyHint;
        public bool IsComposing { get; private set; }
        public event Action HideRequested;
        public event Action SendRequested;
        private bool allowClose;

        public FloatingInputWindow()
        {
            FloatingUi.Configure(this, "语桥 · 中文输入", 480, 324, 390, 282);
            Follow = FloatingUi.FollowControl();
            FullMode = FloatingUi.SmallButton("完整窗口", "打开完整翻译窗口");
            StackPanel content;
            Border shell = FloatingUi.CreateShell(this, "语桥 · 中文输入", Follow, FullMode, RequestHide, out content);
            Content = shell;

            var languageRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,7) };
            languageRow.Children.Add(new TextBlock { Text = "发送语言", Foreground = FloatingUi.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,9,0) });
            LanguageSelector = new ComboBox { Width = 150, FontSize = 12, Padding = new Thickness(5,2,5,2) };
            LanguageSelector.Items.Add("日文 / Japanese");LanguageSelector.Items.Add("英文 / English");LanguageSelector.SelectedIndex=0;
            System.Windows.Automation.AutomationProperties.SetName(LanguageSelector,"发送语言");
            languageRow.Children.Add(LanguageSelector);content.Children.Add(languageRow);

            Grid candidateArea = new Grid { Height = 96 };
            Candidates = new ListBox
            {
                FontSize = 16,
                Background = FloatingUi.Surface,
                Foreground = FloatingUi.Text,
                BorderBrush = FloatingUi.Line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                TabIndex = 1,
                ToolTip = "点击选择日文候选；Alt + ↑ / ↓ 循环选择。"
            };
            System.Windows.Automation.AutomationProperties.SetName(Candidates, "日文候选译文");
            ScrollViewer.SetVerticalScrollBarVisibility(Candidates, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(Candidates, ScrollBarVisibility.Disabled);
            Candidates.ItemContainerStyle = FloatingUi.CandidateStyle();
            FrameworkElementFactory candidateText = new FrameworkElementFactory(typeof(TextBlock));
            candidateText.SetBinding(TextBlock.TextProperty, new Binding("."));
            candidateText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            Candidates.ItemTemplate = new DataTemplate { VisualTree = candidateText };
            candidateArea.Children.Add(Candidates);
            TextBlock empty = new TextBlock
            {
                Text = "输入中文后，日文候选会显示在这里",
                Foreground = FloatingUi.Muted,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            EmptyHint = empty;
            Style placeholder = new Style(typeof(TextBlock));
            placeholder.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
            DataTrigger noCandidates = new DataTrigger { Binding = new Binding("HasItems") { Source = Candidates }, Value = false };
            noCandidates.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
            placeholder.Triggers.Add(noCandidates);
            empty.Style = placeholder;
            candidateArea.Children.Add(empty);
            content.Children.Add(candidateArea);

            Input = FloatingUi.TextBox(false, 16, 44);
            Input.Margin = new Thickness(0, 7, 0, 0);
            Input.TabIndex = 0;
            Input.ToolTip = "Enter：发送日文；Shift + Enter：换行。发送确认后会清空中文，原话留在对话窗。";
            System.Windows.Automation.AutomationProperties.SetName(Input, "中文输入");
            TextCompositionManager.AddPreviewTextInputStartHandler(Input, delegate { IsComposing = true; });
            TextCompositionManager.AddPreviewTextInputUpdateHandler(Input, delegate { IsComposing = true; });
            Input.PreviewTextInput += delegate { IsComposing = false; };
            Input.LostKeyboardFocus += delegate { IsComposing = false; };
            content.Children.Add(Input);

            Grid actions = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Follow.VerticalAlignment = VerticalAlignment.Center;
            actions.Children.Add(Follow);
            Voice = FloatingUi.SmallButton("语音输入", "打开 Windows＋H，识别文字进入中文输入框。");
            Voice.HorizontalAlignment = HorizontalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(Voice, "语音输入");
            Grid.SetColumn(Voice, 1); actions.Children.Add(Voice);
            Fill = FloatingUi.SmallButton("填入日文", "Ctrl + Enter 填入选中日文；不会发送消息。");
            Fill.IsEnabled = false;
            Fill.TabIndex = 2;
            Fill.Background = FloatingUi.Header;
            Fill.BorderBrush = FloatingUi.Line;
            Fill.Foreground = FloatingUi.Text;
            Grid.SetColumn(Fill, 2); actions.Children.Add(Fill);
            content.Children.Add(actions);
            AutoFill = new CheckBox {Content="自动填入 · 回车发送 · Shift + Enter 换行",IsChecked=true,FontSize=11,Margin=new Thickness(0,5,0,0)};
            System.Windows.Automation.AutomationProperties.SetName(AutoFill,"自动填入日文");content.Children.Add(AutoFill);
            Status = FloatingUi.StatusText("说中文或写中文，日文自动填入 Claude，不发送");
            content.Children.Add(Status);

            PreviewKeyDown += HandleShortcut;
            Candidates.PreviewMouseLeftButtonUp += delegate
            {
                // Only an actual mouse choice returns focus to the local composer.
                // Programmatic candidate updates must never steal the user's focus.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (IsVisible) Input.Focus(); }));
            };
        }

        private void HandleShortcut(object sender, KeyEventArgs e)
        {
            if (IsComposing) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.Alt && (key == Key.Up || key == Key.Down))
            {
                MoveCandidate(key == Key.Up ? -1 : 1);
                e.Handled = true;
            }
            else if (modifiers == ModifierKeys.Control && key == Key.Return)
            {
                e.Handled = true;
                if (Fill.IsEnabled) Fill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, Fill));
            }
            else if (modifiers == ModifierKeys.None && key == Key.Return && e.Key != Key.ImeProcessed)
            {
                e.Handled = true;
                if (!e.IsRepeat && SendRequested != null) SendRequested();
            }
        }

        private void MoveCandidate(int delta)
        {
            int count = Candidates.Items.Count;
            if (count == 0) return;
            int selected = Candidates.SelectedIndex;
            Candidates.SelectedIndex = selected < 0 ? (delta < 0 ? count - 1 : 0) : (selected + delta + count) % count;
            Candidates.ScrollIntoView(Candidates.SelectedItem);
        }

        // Normal close returns through FullMode.Click; root hides the floating
        // windows and restores its main window. Shutdown may explicitly release it.
        public void ClosePermanently() { allowClose = true; Close(); }
        void RequestHide() { if (HideRequested != null) HideRequested(); else Hide(); }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!allowClose) { e.Cancel = true; RequestHide(); }
        }
    }

    public sealed class FloatingReadWindow : Window
    {
        public TextBox Chinese;
        public TextBox Japanese;
        public TextBlock Status;
        public CheckBox Follow;
        public Button FullMode;
        public Expander Original;
        public event Action HideRequested;
        private bool allowClose;

        public FloatingReadWindow()
        {
            FloatingUi.Configure(this, "语桥 · 中文阅读", 350, 360, 300, 260);
            Follow = FloatingUi.FollowControl();
            FullMode = FloatingUi.SmallButton("完整窗口", "打开完整翻译窗口");
            StackPanel content;
            Content = FloatingUi.CreateShell(this, "语桥 · 中文对话", Follow, FullMode, RequestHide, out content);
            Chinese = FloatingUi.TextBox(true, 16, 195);
            // Reading text stays unobstructed even when the pointer rests on it.
            ToolTipService.SetIsEnabled(Chinese, false);
            System.Windows.Automation.AutomationProperties.SetName(Chinese, "双方中文对话记录");
            content.Children.Add(Chinese);
            Japanese = FloatingUi.TextBox(true, 13, 120);
            Japanese.Foreground = FloatingUi.Muted;
            Japanese.Margin = new Thickness(0, 6, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(Japanese, "当前回复日文原文");
            Original = new Expander { Header = "查看日文原文", FontSize = 12, Margin = new Thickness(0, 8, 0, 0), IsExpanded = false, Content = Japanese };
            content.Children.Add(Original);
            Status = FloatingUi.StatusText("等待当前最新日文回复…");
            content.Children.Add(Status);
            Follow.Margin = new Thickness(0, 8, 0, 2);
            content.Children.Add(Follow);
        }

        public void ClosePermanently() { allowClose = true; Close(); }
        void RequestHide() { if (HideRequested != null) HideRequested(); else Hide(); }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!allowClose) { e.Cancel = true; RequestHide(); }
        }
    }

    internal static class FloatingUi
    {
        internal static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(226, 175, 142));
        internal static readonly Brush Line = new SolidColorBrush(Color.FromRgb(76, 73, 70));
        internal static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(182, 177, 170));
        internal static readonly Brush Surface = new SolidColorBrush(Color.FromRgb(35, 35, 34));
        internal static readonly Brush Header = new SolidColorBrush(Color.FromRgb(46, 45, 43));
        internal static readonly Brush Text = new SolidColorBrush(Color.FromRgb(239, 235, 228));

        internal static void Configure(Window window, string title, double width, double height, double minWidth, double minHeight)
        {
            window.Title = title;
            window.Width = width; window.Height = height;
            window.MinWidth = minWidth; window.MinHeight = minHeight;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.CanResizeWithGrip;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Topmost = true;
            window.FontFamily = new FontFamily("Microsoft YaHei UI");
            window.FontSize = 13;
            window.Foreground = Text;
            Style buttons = new Style(typeof(Button));
            buttons.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            buttons.Setters.Add(new Setter(Control.BackgroundProperty, Header));
            buttons.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
            var template = new ControlTemplate(typeof(Button));
            var buttonBody = new FrameworkElementFactory(typeof(Border), "buttonBody");
            buttonBody.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            buttonBody.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            buttonBody.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            buttonBody.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            buttonBody.AppendChild(presenter); template.VisualTree = buttonBody;
            var hoverButton = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverButton.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(65, 61, 56)), "buttonBody"));template.Triggers.Add(hoverButton);
            var pressedButton = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedButton.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "buttonBody"));template.Triggers.Add(pressedButton);
            var focusedButton = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focusedButton.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "buttonBody"));template.Triggers.Add(focusedButton);
            var disabledButton = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledButton.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));template.Triggers.Add(disabledButton);
            buttons.Setters.Add(new Setter(Control.TemplateProperty, template));
            window.Resources[typeof(Button)] = buttons;
            window.Resources[typeof(ScrollBar)] = (Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
              <Setter Property='Width' Value='12'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
                <Grid Background='#232322'><Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
                  <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border Background='#68645E' CornerRadius='4' Margin='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
                </Track></Grid>
              </ControlTemplate></Setter.Value></Setter></Style>");
            Style checks = new Style(typeof(CheckBox));checks.Setters.Add(new Setter(Control.ForegroundProperty, Text));window.Resources[typeof(CheckBox)] = checks;
            Style expanders = new Style(typeof(Expander));expanders.Setters.Add(new Setter(Control.ForegroundProperty, Text));window.Resources[typeof(Expander)] = expanders;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
        }

        internal static Border CreateShell(Window window, string title, CheckBox follow, Button fullMode, Action onHide, out StackPanel content)
        {
            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            Grid titlebar = new Grid { Background = Header, Margin = new Thickness(0), Height = 33 };
            titlebar.ColumnDefinitions.Add(new ColumnDefinition());
            titlebar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titlebar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titlebar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titlebar.Children.Add(new TextBlock { Text = title, Foreground = Accent, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 5, 0), IsHitTestVisible = false });
            fullMode.FontSize = 11; fullMode.Margin = new Thickness(0, 3, 3, 3); fullMode.Padding = new Thickness(7, 2, 7, 2);
            Grid.SetColumn(fullMode, 1); titlebar.Children.Add(fullMode);
            Button hide = SmallButton("隐藏", "只隐藏这个小窗，可用顶端开关恢复");
            hide.FontSize = 11; hide.Margin = new Thickness(0, 3, 3, 3); hide.Padding = new Thickness(6, 2, 6, 2); hide.Click += delegate { onHide(); };
            Grid.SetColumn(hide, 2); titlebar.Children.Add(hide);
            Button close = SmallButton("×", "只隐藏这个小窗，不丢失文字");
            close.FontSize = 17; close.Width = 27; close.Margin = new Thickness(0, 3, 4, 3); close.Padding = new Thickness(0); close.Click += delegate { onHide(); };
            Grid.SetColumn(close, 3); titlebar.Children.Add(close);
            InstallDrag(titlebar, window, follow);
            layout.Children.Add(titlebar);
            content = new StackPanel { Margin = new Thickness(10, 9, 10, 9) };
            ScrollViewer scroller = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroller, 1); layout.Children.Add(scroller);
            return new Border
            {
                Margin = new Thickness(5), Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Child = layout,
                Effect = new DropShadowEffect { Color = Color.FromRgb(30, 41, 59), BlurRadius = 8, ShadowDepth = 1, Opacity = 0.22 }
            };
        }

        internal static CheckBox FollowControl() { return new CheckBox { Content = "跟随 Claude", IsChecked = true, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "自动贴近 Claude；拖动标题栏会关闭跟随。" }; }
        internal static Button SmallButton(string text, string tip) { return new Button { Content = text, ToolTip = tip, Padding = new Thickness(10, 4, 10, 4), Foreground = Text, Background = Header, BorderBrush = Line, BorderThickness = new Thickness(1), Cursor = Cursors.Hand }; }
        internal static TextBlock StatusText(string text) { return new TextBlock { Text = text, Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }; }
        internal static TextBox TextBox(bool readOnly, double fontSize, double height) { return new TextBox { IsReadOnly = readOnly, FontSize = fontSize, Height = height, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, BorderBrush = Line, BorderThickness = new Thickness(1), Background = Surface, Foreground = Text, CaretBrush = Accent, SelectionBrush = new SolidColorBrush(Color.FromRgb(105, 76, 56)), Padding = new Thickness(8, 5, 8, 5) }; }

        internal static Style CandidateStyle()
        {
            Style style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, Header)); style.Triggers.Add(hover);
            Trigger selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(68, 51, 40))));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Accent));
            selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); style.Triggers.Add(selected);
            return style;
        }

        private static void InstallDrag(FrameworkElement titlebar, Window window, CheckBox follow)
        {
            titlebar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                DependencyObject source = e.OriginalSource as DependencyObject;
                while (source != null && source != titlebar)
                {
                    if (source is ButtonBase) return;
                    source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
                }
                if (e.ClickCount != 1) return;
                // Start the standard native move loop on mouse down. Waiting for a
                // later WPF mouse-move can miss a quick drag when events coalesce.
                bool followed = follow.IsChecked == true;
                Point before = new Point(window.Left, window.Top);
                follow.IsChecked = false;
                try { window.DragMove(); } catch (InvalidOperationException) { }
                if (Math.Abs(window.Left-before.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(window.Top-before.Y) < SystemParameters.MinimumVerticalDragDistance)
                    follow.IsChecked = followed;
                e.Handled = true;
            };
        }
    }
}
