using Amplifier.Core;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Amplifier.Windows;

internal sealed record ChatChoice(Registration Service, string Id, string Title)
{
    public override string ToString() => Service.Label + " · " + Title;
}
internal sealed class ComposerWindow : Window
{
    private readonly ComboBox target = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBox editor = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button send = new() { Content = "Send", Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(16, 7, 16, 7) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    public ComposerWindow(ClientController owner)
    {
        Title = "Message · Amplifier";Width = 560;Height = 440;MinWidth = 400;MinHeight = 350;WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var stack = new StackPanel { Margin = new Thickness(22) };Content = new ScrollViewer { Content = stack };
        foreach (var runtime in owner.Services.Where(x => x.Observation != null))
            foreach (var session in runtime.Observation!.Sessions.Where(x => x.AgentCreated != true || x.Pinned))
                target.Items.Add(new ChatChoice(runtime.Registration, session.Id, session.Title));
        if (target.Items.Count > 0) target.SelectedIndex = 0;
        stack.Children.Add(target);stack.Children.Add(editor);stack.Children.Add(send);stack.Children.Add(status);
        var savedPanel = new StackPanel();stack.Children.Add(savedPanel);
        send.Click += async (_, _) =>
        {
            if (target.SelectedItem is not ChatChoice choice) { status.Text = "Choose a connected chat. Open the workspace to start a new one.";return; }
            send.IsEnabled = false;target.IsEnabled = false;
            try
            {
                var saved = owner.Outbox.Save(choice.Service, choice.Id, editor.Text);editor.Clear();status.Text = "Saved. Sending…";
                await owner.SendAsync(saved);status.Text = "Accepted by the host.";
            }
            catch (Exception error) { status.Text = error.Message + " Review any saved message below before retrying."; }
            finally { send.IsEnabled = true;target.IsEnabled = true;RefreshSaved(); }
        };
        void RefreshSaved()
        {
            savedPanel.Children.Clear();
            foreach (var reply in owner.Outbox.Entries.Where(x => x.Status != "accepted"))
            {
                var label = new TextBlock { Text = "Saved for " + new Uri(reply.Endpoint).Authority + " · " + reply.SessionId, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 4) };savedPanel.Children.Add(label);
                var review = new Button { Content = "Review saved message…" };
                review.Click += async (_, _) =>
                {
                    if (MessageBox.Show(reply.Text + "\n\nRetry this exact saved message to its original chat? If delivery was uncertain, check the chat first.", "Saved message · Amplifier", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    review.IsEnabled = false;
                    try { await owner.SendAsync(reply);status.Text = "Accepted by the host."; }
                    catch (Exception e) { status.Text = e.Message; }
                    finally { RefreshSaved(); }
                };
                savedPanel.Children.Add(review);
            }
        }
        RefreshSaved();
        Closing += (_, e) => { if (!string.IsNullOrWhiteSpace(editor.Text) && MessageBox.Show("Discard this unsent draft?", "Amplifier", MessageBoxButton.YesNo) != MessageBoxResult.Yes) e.Cancel = true; };
    }
}
