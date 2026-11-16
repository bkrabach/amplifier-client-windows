using Amplifier.Core;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Amplifier.Windows;

internal sealed class TempoWindow : Window, IDisposable
{
    private readonly ClientController owner;
    private readonly AvatarVisual avatar = new() { Width = 90, Height = 90 };
    private readonly AvatarPlayback playback = new();
    private readonly HoverPresentation hover = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly Button pill;
    private readonly NoticeWindow notices;
    private Point? dragStart;
    private Point startPosition;
    public TempoWindow(ClientController owner)
    {
        this.owner = owner;Title = owner.Preview ? "Amplifier Tempo Preview" : "Amplifier Tempo";Width = 100;Height = 111;WindowStyle = WindowStyle.None;AllowsTransparency = true;
        Background = Brushes.Transparent;ShowInTaskbar = false;ShowActivated = false;Topmost = true;ResizeMode = ResizeMode.NoResize;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(avatar);
        var pillBorder = new FrameworkElementFactory(typeof(Border));
        pillBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        pillBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        pill = new Button { Width = 34, Height = 10, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2.4, 0, 0), Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Template = new ControlTemplate(typeof(Button)) { VisualTree = pillBorder } };
        pill.Click += (_, _) => { hover.Toggle();owner.Preferences.Visibility = hover.Mode;owner.PreferencesChanged(); };stack.Children.Add(pill);Content = stack;
        notices = new NoticeWindow(owner, this);
        SourceInitialized += (_, _) => Native.Passive(this);
        Loaded += (_, _) =>
        {
            var work = Native.WorkArea(this);Left = owner.Preferences.AvatarLeft ?? work.Right - Width - 24;Top = owner.Preferences.AvatarTop ?? work.Bottom - Height - 24;Clamp();Refresh();
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start();else { timer.Stop();notices.Hide(); } };
        avatar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { owner.OpenDefault();return; }
            dragStart = Native.Cursor(this);startPosition = new Point(Left, Top);avatar.CaptureMouse();e.Handled = true;
        };
        avatar.MouseMove += (_, _) =>
        {
            if (dragStart is Point start && Mouse.LeftButton == MouseButtonState.Pressed)
            { var p = Native.Cursor(this);Left = startPosition.X + p.X - start.X;Top = startPosition.Y + p.Y - start.Y;Clamp();LayoutNotices(); }
        };
        avatar.MouseLeftButtonUp += (_, _) =>
        {
            if (dragStart == null) return;dragStart = null;avatar.ReleaseMouseCapture();owner.Preferences.AvatarLeft = Left;owner.Preferences.AvatarTop = Top;owner.Save();
        };
        timer.Interval = TimeSpan.FromSeconds(1.0 / 48);timer.Tick += (_, _) => Tick();
        ContextMenu = new ContextMenu();
        var settings = new MenuItem { Header = "Settings…" };settings.Click += (_, _) => owner.OpenSettings();ContextMenu.Items.Add(settings);
        var hide = new MenuItem { Header = "Hide Tempo" };hide.Click += (_, _) => owner.ToggleTempo();ContextMenu.Items.Add(hide);
    }
    private void Clamp()
    {
        var area = Native.WorkArea(this);Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }
    public void Refresh()
    {
        hover.Mode = owner.Preferences.Visibility;
        avatar.Load(owner.Preferences.Shape, owner.Preferences.Finish, owner.Preferences.Palette);
        playback.Update(owner.Connected, owner.Connecting, owner.Working, owner.AllRows, clock.Elapsed.TotalSeconds);
        var state = playback.State;
        pill.Background = new SolidColorBrush(state switch
        {
            AvatarState.Error => Color.FromRgb(240, 65, 80), AvatarState.Review or AvatarState.Ready => Color.FromRgb(0, 120, 215),
            AvatarState.Working => Color.FromRgb(0, 170, 170), AvatarState.Offline or AvatarState.Connecting => Color.FromRgb(205, 145, 55), _ => Colors.Gray
        });
        AutomationProperties.SetName(avatar, owner.Preferences.Shape + ", " + state);
        AutomationProperties.SetName(pill, state + ". Show or hide updates");pill.ToolTip = state + " · " + owner.Attention + " updates";
        notices.UpdateRows();Tick();
    }
    private void Tick()
    {
        if (!IsVisible) return;
        var presentation = playback.Presentation(clock.Elapsed.TotalSeconds);avatar.Show(presentation.State, presentation.Elapsed);
        if (hover.Mode == NoticeVisibility.Hover)
        {
            var pointer = Native.Cursor(this);
            var inside = new Rect(Left, Top, Width, Height).Contains(pointer) || (notices.IsVisible && new Rect(notices.Left, notices.Top, notices.Width, notices.Height).Contains(pointer));
            hover.Update(inside, dragStart != null || notices.Resizing, clock.Elapsed);
        }
        else hover.Update(false, false, clock.Elapsed);
        if (hover.Visible && owner.Rows.Count > 0 && Top - Native.WorkArea(this).Top >= 38) { LayoutNotices();if (!notices.IsVisible) notices.Show(); }
        else notices.Hide();
    }
    public void LayoutNotices()
    {
        var area = Native.WorkArea(this);notices.ArrangeAbove(Left + Width, Top, area);
    }
    public void Dispose() { timer.Stop();notices.Close();avatar.Dispose();Close(); }
}

internal sealed class NoticeWindow : Window
{
    private readonly ClientController owner;
    private readonly TempoWindow avatar;
    private readonly StackPanel cards = new();
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
    private readonly TextBlock heading = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly Border header;
    private readonly RowDefinition viewport = new();
    private string rendered = "";
    private Point? resizeStart;
    private double startHeight;
    private bool moved;
    public bool Resizing => resizeStart != null;
    public NoticeWindow(ClientController owner, TempoWindow avatar)
    {
        this.owner = owner;this.avatar = avatar;Width = 340;Height = 100;WindowStyle = WindowStyle.None;AllowsTransparency = true;Background = Brushes.Transparent;
        ShowInTaskbar = false;ShowActivated = false;Topmost = true;ResizeMode = ResizeMode.NoResize;
        var grid = new Grid();grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });grid.RowDefinitions.Add(viewport);
        header = new Border { Background = new SolidColorBrush(Color.FromArgb(235, 40, 48, 58)), CornerRadius = new CornerRadius(15), Child = heading, Cursor = Cursors.SizeNS };
        heading.Foreground = Brushes.White;grid.Children.Add(header);
        scroll.Content = cards;scroll.Background = Brushes.Transparent;Grid.SetRow(scroll, 1);grid.Children.Add(scroll);Content = grid;
        SourceInitialized += (_, _) => Native.Passive(this);
        header.MouseLeftButtonDown += (_, e) => { resizeStart = Native.Cursor(this);startHeight = scroll.Height; moved = false;header.CaptureMouse();e.Handled = true; };
        header.MouseMove += (_, _) =>
        {
            if (resizeStart is not Point point || Mouse.LeftButton != MouseButtonState.Pressed) return;
            var delta = point.Y - Native.Cursor(this).Y;if (Math.Abs(delta) < 4 && !moved) return;moved = true;
            owner.Preferences.Expanded = true;
            var desired = Math.Max(0, startHeight + delta);var content = ContentHeight();
            owner.Preferences.FitAll = desired >= content;
            owner.Preferences.MaximumHeight = Math.Min(content, desired);avatar.LayoutNotices();
        };
        header.MouseLeftButtonUp += (_, _) =>
        {
            if (resizeStart == null) return;resizeStart = null;header.ReleaseMouseCapture();
            if (!moved) owner.Preferences.Expanded = !owner.Preferences.Expanded;
            owner.PreferencesChanged();
        };
        scroll.ScrollChanged += (_, _) => Fade();
        // Transparent viewport deliberately catches wheel events, including the gaps.
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * .5);e.Handled = true; };
    }
    private double ContentHeight() => owner.Preferences.Expanded ? owner.Rows.Count * 66 + (owner.Preferences.GroupByService && owner.Preferences.ServiceHeadings ? owner.Rows.Select(x => x.RegistrationId).Distinct().Count() * 24 : 0) : 62;
    public void ArrangeAbove(double right, double anchor, Rect area)
    {
        var content = ContentHeight();var height = NoticeGeometry.Height(content, owner.Preferences.MaximumHeight, owner.Preferences.FitAll, anchor, area.Top);
        Width = Math.Min(340, area.Width);scroll.Height = height;viewport.Height = new GridLength(height);Height = height + 30;
        Left = Math.Clamp(right - Width, area.Left, Math.Max(area.Left, area.Right - Width));Top = anchor - Height - 8;
        heading.Text = owner.Attention + " updates · " + (owner.Preferences.Expanded ? "⌃ Collapse" : "⌄ Expand");
        Fade();
    }
    public void UpdateRows()
    {
        var key = owner.Preferences.Expanded + ":" + owner.Preferences.GroupByService + ":" + owner.Preferences.ServiceHeadings + ":" + string.Join("|", owner.Rows.Select(x => x.Key + x.Fingerprint + x.Detail + x.ServiceName));
        if (key == rendered) return;rendered = key;
        var offset = scroll.VerticalOffset;cards.Children.Clear();string? previousService = null;
        foreach (var row in owner.Preferences.Expanded ? owner.Rows : owner.Rows.Take(1))
        {
            if (owner.Preferences.Expanded && owner.Preferences.GroupByService && owner.Preferences.ServiceHeadings && previousService != row.RegistrationId)
            { cards.Children.Add(new TextBlock { Text = row.ServiceName, Foreground = Brushes.LightGray, Height = 24, Margin = new Thickness(12, 0, 0, 0) });previousService = row.RegistrationId; }
            var body = new Grid { Margin = new Thickness(12, 7, 8, 7) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            var icon = new TextBlock { Text = row.State switch { AvatarState.Working => "◌", AvatarState.Review => "?", AvatarState.Error => "!", _ => "●" }, Foreground = row.State is AvatarState.Review or AvatarState.Error ? Brushes.Orange : Brushes.DeepSkyBlue, FontSize = 18, VerticalAlignment = VerticalAlignment.Center };body.Children.Add(icon);
            var text = new StackPanel();text.Children.Add(new TextBlock { Text = row.Title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = (owner.Preferences.Services.Count > 1 ? row.ServiceName + " · " : "") + row.Detail, Foreground = Brushes.LightGray, TextTrimming = TextTrimming.CharacterEllipsis });
            var open = new Button { Content = text, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(open, row.Title + ", " + row.Detail + ". Open chat");open.Click += (_, _) => owner.OpenChat(row);Grid.SetColumn(open, 1);body.Children.Add(open);
            var close = new Button { Content = "×", Foreground = Brushes.LightGray, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), FontSize = 20 };
            close.ToolTip = row.State == AvatarState.Working ? "Hide until activity changes" : "Hide from Tempo; keep unread";AutomationProperties.SetName(close, "Dismiss " + row.Title);close.Click += (_, _) => owner.Dismiss(row);Grid.SetColumn(close, 2);body.Children.Add(close);
            cards.Children.Add(new Border { Height = 58, Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Color.FromArgb(238, 38, 44, 52)), CornerRadius = new CornerRadius(27), Child = body });
        }
        Dispatcher.BeginInvoke(() => { scroll.ScrollToVerticalOffset(offset);Fade(); }, DispatcherPriority.Loaded);
    }
    private void Fade()
    {
        var fade = Math.Min(.4, 18 / Math.Max(1, scroll.Height));
        var top = scroll.VerticalOffset > .5;var bottom = scroll.ScrollableHeight - scroll.VerticalOffset > .5;
        scroll.OpacityMask = new LinearGradientBrush(new GradientStopCollection
        { new(top ? Colors.Transparent : Colors.Black, 0), new(Colors.Black, fade), new(Colors.Black, 1 - fade), new(bottom ? Colors.Transparent : Colors.Black, 1) }, new Point(0, 0), new Point(0, 1));
    }
}
