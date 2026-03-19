namespace Keybusy_DiskScope.Services;

/// <summary>
/// Abstracts Frame navigation so ViewModels never reference Microsoft.UI.Xaml.
/// The Frame is registered via SetFrame() from MainWindow code-behind.
/// </summary>
public interface INavigationService
{
    bool CanGoBack { get; }
    void NavigateTo(Type pageType, object? parameter = null);
    void NavigateTo(string pageKey, object? parameter = null);
    void GoBack();

    // Called from the window to supply the Frame used for navigation
    void SetFrame(object frame);
}
