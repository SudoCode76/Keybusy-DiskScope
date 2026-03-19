using Keybusy_DiskScope.Services.Implementation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Keybusy_DiskScope.Views;

public sealed partial class ShellPage : Page
{
    private static readonly Dictionary<string, Type> _pages = new()
    {
        { "HomePage",      typeof(HomePage) },
        { "ScanPage",      typeof(ScanPage) },
        { "SnapshotsPage", typeof(SnapshotsPage) },
        { "ComparePage",   typeof(ComparePage) },
    };

    public ShellPage()
    {
        InitializeComponent();

        // Register the ContentFrame with the navigation service via the interface
        var navService = App.Services.GetRequiredService<Services.INavigationService>();
        navService.SetFrame(ContentFrame);
    }

    private void NavView_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ContentFrame.Navigated += ContentFrame_Navigated;
        Navigate("HomePage");

        // Designate the title bar drag region
        var window = (Application.Current as App)?.MainWindow;
        window?.SetTitleBar(AppTitleBar);
    }

    private void NavView_ItemInvoked(NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag)
            Navigate(tag);
    }

    private void NavView_BackRequested(NavigationView sender,
        NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        NavView.IsBackEnabled = ContentFrame.CanGoBack;
        var pageType = e.SourcePageType;
        var tag = _pages.FirstOrDefault(p => p.Value == pageType).Key;

        var allItems = NavView.MenuItems
            .OfType<NavigationViewItem>();

        NavView.SelectedItem = allItems.FirstOrDefault(i => i.Tag?.ToString() == tag);
    }

    private void Navigate(string tag)
    {
        if (_pages.TryGetValue(tag, out var pageType) &&
            ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
