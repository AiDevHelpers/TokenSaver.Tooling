using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Extensibility.UI;

namespace TokenSaver.Vs.Extension.Pages;

/// <summary>
/// One account in the Settings "Users" card: a current member of the TokenSaver group (button removes it) or a local
/// account that could be added (button adds it).
/// </summary>
[DataContract]
internal sealed class AuthUserRow : NotifyPropertyChangedObject
{
    public AuthUserRow(string account, bool isMember, Func<AuthUserRow, Task> toggle)
    {
        Account = account;
        IsMember = isMember;
        ToggleCommand = new AsyncCommand(async (_, _) => await toggle(this).ConfigureAwait(false));
    }

    [DataMember] public string Account { get; }

    [DataMember] public bool IsMember { get; }

    [DataMember] public string ActionText => IsMember ? "Remove" : "Add";

    [DataMember] public AsyncCommand ToggleCommand { get; }
}
