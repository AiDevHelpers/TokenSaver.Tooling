using Microsoft.VisualStudio.Extensibility.UI;

namespace TokenSaver.Vs.Extension.Pages;

/// <summary>
/// Remote UI content of <see cref="TokenSaverToolWindow"/>. Its XAML is the embedded resource
/// TokenSaverToolWindowContent.xaml, which must keep this type's full name so Remote UI can find it.
/// </summary>
internal sealed class TokenSaverToolWindowContent(TokenSaverWindowViewModel dataContext) : RemoteUserControl(dataContext)
{
}
