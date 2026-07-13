using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using TokenSaver.Vs.Extension.Services;

namespace TokenSaver.Vs.Extension.Chat;

/// <summary>
/// Persistent, non-modal chat window for DraftModel — the dedicated chat surface referenced by
/// <see cref="TokenSaver.Vs.Extension.Commands.OpenChatCommand"/>. Unlike the per-selection
/// DraftModel editor commands (which stream one-shot responses to the Output window), this keeps
/// a running conversation and lets the user keep working in the editor while it's open.
/// </summary>
public sealed partial class ChatWindow : Window, IDisposable
{
    private static readonly Uri DefaultBaseUrl = new("http://localhost:11435");

    private readonly ObservableCollection<ChatMessageDisplay> _messages = [];
    private readonly List<DraftModelChatMessage> _history = [];
    private readonly IDraftModelClient _client;
    private readonly CancellationTokenSource _cts = new();

    public ChatWindow()
        : this(null)
    {
    }

    internal ChatWindow(IDraftModelClient? client)
    {
        _client = client ?? new HttpDraftModelClient(DefaultBaseUrl);
        InitializeComponent();
        MessagesList.ItemsSource = _messages;
        Closed += (_, _) => Dispose();
        Loaded += (_, _) => InputTextBox.Focus();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        (_client as IDisposable)?.Dispose();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = SendAsync();
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

    private async Task SendAsync()
    {
        var text = InputTextBox.Text.Trim();
        if (text.Length == 0 || _cts.IsCancellationRequested)
        {
            return;
        }

        InputTextBox.Clear();
        SendButton.IsEnabled = false;

        var userMessage = new ChatMessageDisplay(DraftModelChatRole.User) { Text = text };
        _messages.Add(userMessage);
        _history.Add(new DraftModelChatMessage(DraftModelChatRole.User, text));
        ScrollToEnd();

        var assistantMessage = new ChatMessageDisplay(DraftModelChatRole.Assistant);
        _messages.Add(assistantMessage);

        try
        {
            var health = await _client.CheckHealthAsync(_cts.Token).ConfigureAwait(true);
            if (health.Status == DraftModelHealthStatus.Unreachable)
            {
                assistantMessage.Text =
                    $"DraftModel is not reachable at {DefaultBaseUrl}. Start it via " +
                    "'TokenSaver: Start TokenSaver MCP Server'.";
                return;
            }

            var responseText = string.Empty;
            await foreach (var chunk in _client.StreamChatAsync(_history, _cts.Token).ConfigureAwait(true))
            {
                if (chunk.ContentDelta.Length > 0)
                {
                    assistantMessage.Append(chunk.ContentDelta);
                    responseText += chunk.ContentDelta;
                    ScrollToEnd();
                }
            }

            _history.Add(new DraftModelChatMessage(DraftModelChatRole.Assistant, responseText));
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-stream.
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            assistantMessage.Text = $"DraftModel request failed: {ex.Message}";
        }
        finally
        {
            if (!_cts.IsCancellationRequested)
            {
                SendButton.IsEnabled = true;
                ScrollToEnd();
            }
        }
    }

    private void ScrollToEnd() => HistoryScrollViewer.ScrollToEnd();
}
