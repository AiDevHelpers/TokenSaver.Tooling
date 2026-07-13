using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

using TokenSaver.Vs.Extension.Services;

namespace TokenSaver.Vs.Extension.Chat;

/// <summary>One chat bubble in <see cref="ChatWindow"/>. Mutable so streamed tokens can append to
/// <see cref="Text"/> in place instead of replacing the assistant's bubble every chunk.</summary>
public sealed class ChatMessageDisplay(DraftModelChatRole role) : INotifyPropertyChanged
{
    private string _text = string.Empty;

    public DraftModelChatRole Role { get; } = role;

    public string RoleLabel => Role switch
    {
        DraftModelChatRole.User => "You",
        DraftModelChatRole.Assistant => "DraftModel",
        _ => "System",
    };

    public Brush Background => Role switch
    {
        DraftModelChatRole.User => new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
        DraftModelChatRole.Assistant => new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F)),
        _ => new SolidColorBrush(Color.FromRgb(0x5F, 0x1E, 0x1E)),
    };

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            OnPropertyChanged();
        }
    }

    public void Append(string delta)
    {
        Text += delta;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
