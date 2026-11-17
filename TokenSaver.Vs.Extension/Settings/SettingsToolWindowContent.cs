using Microsoft.VisualStudio.Extensibility.UI;

namespace TokenSaver.Vs.Extension.Settings;

/// <summary>
/// Remote UI content of <see cref="SettingsToolWindow"/>. Its XAML is the embedded resource
/// SettingsToolWindowContent.xaml, which must keep this type's full name so Remote UI can find it.
/// </summary>
internal sealed class SettingsToolWindowContent(SettingsViewModel dataContext) : RemoteUserControl(dataContext)
{
}
