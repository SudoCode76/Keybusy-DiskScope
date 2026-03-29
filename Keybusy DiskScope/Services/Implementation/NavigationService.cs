using Microsoft.UI.Xaml.Controls;

namespace Keybusy_DiskScope.Services.Implementation;

/// <summary>
/// NavigationService implementation. Requires the app Frame to be injected
/// from MainWindow code-behind via <see cref="SetFrame"/>.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private static readonly Dictionary<string, Type> PageMap = new()
    {
        { "HomePage", typeof(Views.HomePage) },
        { "ScanPage", typeof(Views.ScanPage) },
        { "SnapshotsPage", typeof(Views.SnapshotsPage) },
        { "ComparePage", typeof(Views.ComparePage) },
        { "AboutPage", typeof(Views.AboutPage) },
        { "SettingsPage", typeof(Views.SettingsPage) }
    };

    private Frame? _frame;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    /// <summary>Called once from MainWindow after InitializeComponent.</summary>
    // Keep a strongly-typed helper for internal callers
    public void SetFrame(Frame frame) => _frame = frame;

    // Implement the interface method that accepts an object (the interface is UI-agnostic)
    public void SetFrame(object frame)
    {
        if (frame is Frame f)
        {
            _frame = f;
            return;
        }

        throw new ArgumentException("frame must be a Microsoft.UI.Xaml.Controls.Frame", nameof(frame));
    }

    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame is null) throw new InvalidOperationException("Frame not set. Call SetFrame() first.");
        _frame.Navigate(pageType, parameter);
    }

    public void NavigateTo(string pageKey, object? parameter = null)
    {
        if (!PageMap.TryGetValue(pageKey, out var pageType))
        {
            throw new ArgumentException($"Unknown page key: {pageKey}", nameof(pageKey));
        }

        NavigateTo(pageType, parameter);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }
}
