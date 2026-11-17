using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility.UI;

namespace TokenSaver.Vs.Extension.Pages;

/// <summary>
/// One extra model endpoint on the Settings tab: a name, the server URL, the models to use there, and whether it is the
/// primary one. Exactly one row may be primary, which the owning <see cref="SettingsViewModel"/> enforces.
/// </summary>
[DataContract]
internal sealed class EndpointRow : NotifyPropertyChangedObject
{
    private readonly Action<EndpointRow> _changed;
    private string _name = "";
    private string _url = "";
    private string _models = "";
    private bool _isPrimary;
    private string _error = "";

    public EndpointRow(Action<EndpointRow> changed, Action<EndpointRow> remove)
    {
        _changed = changed;
        RemoveCommand = new AsyncCommand((_, _) =>
        {
            remove(this);
            return Task.CompletedTask;
        });
    }

    [DataMember] public AsyncCommand RemoveCommand { get; }

    [DataMember] public string Name { get => _name; set => Set(ref _name, value); }

    [DataMember] public string Url { get => _url; set => Set(ref _url, value); }

    /// <summary>Model names, separated by commas.</summary>
    [DataMember] public string Models { get => _models; set => Set(ref _models, value); }

    [DataMember] public bool IsPrimary { get => _isPrimary; set => Set(ref _isPrimary, value); }

    [DataMember] public string Error { get => _error; set => SetProperty(ref _error, value); }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name))
        {
            _changed(this);
        }
    }
}
