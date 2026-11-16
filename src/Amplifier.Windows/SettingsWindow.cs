using Amplifier.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Amplifier.Windows;

internal sealed class SettingsWindow : Window
{
    private readonly ClientController owner;
    private readonly StackPanel serviceList = new();
    private readonly TextBox address = new() { MinWidth = 340 };
    private readonly TextBox name = new() { MinWidth = 340 };
    private readonly TextBlock pairingStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button connect = new() { Content = "Connect in browser", Padding = new Thickness(16, 8, 16, 8) };
    private readonly List<AvatarVisual> previews = new();
    private CancellationTokenSource? pairing;
    private bool closed;
    public SettingsWindow(ClientController owner)
    {
        this.owner = owner;Title = owner.Preview ? "Amplifier Preview Settings · synthetic activity" : "Amplifier Settings";Width = 720;Height = 610;MinWidth = 640;MinHeight = 500;WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var tabs = new TabControl { Margin = new Thickness(18) };Content = tabs;
        var appearance = new StackPanel { Margin = new Thickness(22) };
        appearance.Children.Add(new TextBlock { Text = "Make Tempo yours", FontSize = 24, FontWeight = FontWeights.SemiBold });
        appearance.Children.Add(new TextBlock { Text = "Choose a shape, color and glass. Changes appear immediately.", Margin = new Thickness(0, 8, 0, 16) });
        var finishes = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var finish in new[] { "clear", "dark" })
        {
            var choice = new RadioButton { Content = char.ToUpper(finish[0]) + finish[1..], GroupName = "finish", IsChecked = owner.Preferences.Finish == finish, Margin = new Thickness(0, 0, 24, 12) };
            choice.Checked += (_, _) => { owner.Preferences.Finish = finish;AppearanceChanged(); };finishes.Children.Add(choice);
        }
        appearance.Children.Add(finishes);
        var colors = new WrapPanel();
        foreach (var palette in Presentation.Palettes)
        {
            var color = palette switch { "blue" => Colors.DodgerBlue, "cyan" => Colors.Cyan, "amber" => Colors.Orange, "crimson" => Colors.Crimson, "green" => Colors.MediumSeaGreen, "purple" => Colors.MediumPurple, "cyan-amber" => Colors.Turquoise, _ => Colors.BlueViolet };
            var swatch = new Button { Width = 34, Height = 28, Margin = new Thickness(0, 0, 10, 12), Background = new SolidColorBrush(color), ToolTip = palette };
            AutomationProperties.SetName(swatch, palette + " color");swatch.Click += (_, _) => { owner.Preferences.Palette = palette;AppearanceChanged(); };colors.Children.Add(swatch);
        }
        appearance.Children.Add(colors);
        var shapes = new UniformGrid { Columns = 3, Rows = 2 };
        foreach (var shape in Presentation.Shapes)
        {
            var art = new AvatarVisual { Width = 92, Height = 92, Tag = shape };previews.Add(art);
            var card = new StackPanel();card.Children.Add(art);card.Children.Add(new TextBlock { Text = shape == "d20" ? "d20" : char.ToUpper(shape[0]) + shape[1..], HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Content = card, Margin = new Thickness(4), Padding = new Thickness(8) };AutomationProperties.SetName(button, shape + " avatar");button.Click += (_, _) => { owner.Preferences.Shape = shape;owner.PreferencesChanged(); };shapes.Children.Add(button);
        }
        appearance.Children.Add(shapes);tabs.Items.Add(new TabItem { Header = "Appearance", Content = appearance });

        var tempo = new StackPanel { Margin = new Thickness(22) };
        tempo.Children.Add(new TextBlock { Text = "Notices around Tempo", FontSize = 24, Margin = new Thickness(0, 0, 0, 20) });
        var visibility = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var mode in Enum.GetValues<NoticeVisibility>())
        {
            var radio = new RadioButton { Content = mode == NoticeVisibility.Hover ? "On hover" : mode.ToString(), GroupName = "notices", IsChecked = owner.Preferences.Visibility == mode, Margin = new Thickness(0, 0, 24, 20) };
            radio.Checked += (_, _) => { owner.Preferences.Visibility = mode;owner.PreferencesChanged(); };visibility.Children.Add(radio);
        }
        tempo.Children.Add(visibility);
        tempo.Children.Add(new TextBlock { Text = "Hover over the avatar or notices to reveal the saved collapsed or expanded list.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24) });
        Toggle(tempo, "Include ongoing work", owner.Preferences.IncludeWorking, v => owner.Preferences.IncludeWorking = v);
        Toggle(tempo, "Group notices by service", owner.Preferences.GroupByService, v => owner.Preferences.GroupByService = v);
        Toggle(tempo, "Show service headings when grouped", owner.Preferences.ServiceHeadings, v => owner.Preferences.ServiceHeadings = v);
        tempo.Children.Add(new TextBlock { Text = "Without grouping, attention is sorted across all services. Drag the notice header to resize; reach the complete list to follow its size automatically.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 24, 0, 0) });
        tabs.Items.Add(new TabItem { Header = "Tempo", Content = tempo });

        var connection = new StackPanel { Margin = new Thickness(22) };
        connection.Children.Add(new TextBlock { Text = "Your Amplifier services", FontSize = 24, Margin = new Thickness(0, 0, 0, 12) });connection.Children.Add(serviceList);
        connection.Children.Add(new TextBlock { Text = "Add a service", FontSize = 18, Margin = new Thickness(0, 16, 0, 8) });
        connection.Children.Add(new Label { Content = "Service address", Target = address });connection.Children.Add(address);
        AutomationProperties.SetName(address, "Amplifier service address");
        connection.Children.Add(new Label { Content = "Name (optional)", Target = name });connection.Children.Add(name);
        AutomationProperties.SetName(name, "Service name");
        connection.Children.Add(pairingStatus);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };actions.Children.Add(connect);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(8, 0, 0, 0) };cancel.Click += (_, _) => pairing?.Cancel();actions.Children.Add(cancel);connection.Children.Add(actions);
        connect.IsEnabled = !owner.Preview;
        if (owner.Preview) pairingStatus.Text = "Connections are disabled in this isolated animation preview.";
        connect.Click += async (_, _) => await PairAsync();
        tabs.Items.Add(new TabItem { Header = "Connections", Content = new ScrollViewer { Content = connection } });

        var about = new TextBlock { Margin = new Thickness(22), TextWrapping = TextWrapping.Wrap, Text = "Amplifier for Windows · development preview\n\nThis native client observes and replies to host-owned chats. It does not install a server or grant browser/desktop control.\n\nWindows notification activation, signed installation, update delivery, accessibility and real-device acceptance are release gates still under development." };
        tabs.Items.Add(new TabItem { Header = "About", Content = about });
        owner.Changed += UpdateServices;Closed += (_, _) => { closed = true;pairing?.Cancel();owner.Changed -= UpdateServices;foreach (var art in previews) art.Dispose(); };
        LoadPreviews();UpdateServices();if (owner.Preferences.Services.Count == 0) tabs.SelectedIndex = 2;
    }
    private void Toggle(Panel panel, string label, bool initial, Action<bool> update)
    {
        var control = new CheckBox { Content = label, IsChecked = initial, Margin = new Thickness(0, 0, 0, 18) };
        control.Click += (_, _) => { update(control.IsChecked == true);owner.PreferencesChanged(); };panel.Children.Add(control);
    }
    private void AppearanceChanged() { owner.PreferencesChanged();LoadPreviews(); }
    private void LoadPreviews() { foreach (var art in previews) { art.Load((string)art.Tag, owner.Preferences.Finish, owner.Preferences.Palette);art.Show(AvatarState.Offline, 0); } }
    private string renderedServices = "";
    private void UpdateServices()
    {
        var value = string.Join("|", owner.Preferences.Services.Select(x => x.Id + x.Name + x.Paused + owner.Status(x.Id)));
        if (renderedServices == value) return;renderedServices = value;serviceList.Children.Clear();
        foreach (var registration in owner.Preferences.Services)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var forget = new Button { Content = "Forget", Margin = new Thickness(8, 0, 0, 0) };forget.IsEnabled = !owner.Preview;forget.Click += (_, _) => Act(() => owner.Forget(registration));DockPanel.SetDock(forget, Dock.Right);row.Children.Add(forget);
            var pause = new Button { Content = registration.Paused ? "Resume" : "Disconnect", Margin = new Thickness(8, 0, 0, 0) };pause.IsEnabled = !owner.Preview;pause.Click += (_, _) => Act(() => owner.Pause(registration));DockPanel.SetDock(pause, Dock.Right);row.Children.Add(pause);
            row.Children.Add(new TextBlock { Text = registration.Label + "\n" + owner.Status(registration.Id), VerticalAlignment = VerticalAlignment.Center });serviceList.Children.Add(row);
        }
    }
    private static void Act(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "Amplifier"); } }
    private async Task PairAsync()
    {
        pairing?.Cancel();pairing = new CancellationTokenSource();var active = pairing;connect.IsEnabled = false;
        try
        {
            var endpoint = new Endpoint(address.Text);var label = name.Text;
            using var transport = new HostTransport(endpoint);var client = new PairingClient(transport);
            var request = await client.BeginAsync(active.Token);
            if (closed || pairing != active) return;
            pairingStatus.Text = "Compare this code in your browser: " + request.Code + "\nApprove chat access only if it matches.";
            owner.Open(endpoint.Resource(request.VerificationPath));
            var token = await client.CompleteAsync(request, active.Token);
            if (closed || pairing != active) return;
            await owner.RegisterAsync(endpoint, label, token, active.Token);
            pairingStatus.Text = "Connected. Browser and desktop execution remain separate permissions.";
            address.Clear();name.Clear();
        }
        catch (OperationCanceledException) { if (!closed) pairingStatus.Text = "Connection request canceled or expired. Saved services are unchanged."; }
        catch (Exception error) { if (!closed) pairingStatus.Text = error.Message; }
        finally { if (pairing == active) { pairing = null;connect.IsEnabled = true; }active.Dispose(); }
    }
}
