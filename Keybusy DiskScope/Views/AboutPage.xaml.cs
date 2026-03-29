using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Keybusy_DiskScope.Views;

public sealed partial class AboutPage : Page
{
    private static readonly Uri GitHubUri = new("https://github.com/SudoCode76");
    private static readonly Uri LinkedInUri = new("https://www.linkedin.com/in/miguel-zenteno/");
    private static readonly Uri WhatsAppUri = new("https://wa.link/98my59");

    public AboutPage()
    {
        InitializeComponent();
    }

    private async void OpenGitHub_Click(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(GitHubUri);

    private async void OpenLinkedIn_Click(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(LinkedInUri);

    private async void OpenWhatsApp_Click(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(WhatsAppUri);
}
