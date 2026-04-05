using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Keybusy_DiskScope.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Keybusy_DiskScope.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void RelaunchAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsRunningAsAdministrator())
            {
                ViewModel.DriveDiagnostics.Add("La aplicacion ya se esta ejecutando como administrador.");
                return;
            }

            if (IsPackaged())
            {
                RelaunchPackagedAsAdmin();
                Application.Current.Exit();
                return;
            }

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                ViewModel.DriveDiagnostics.Add("No se pudo obtener la ruta del ejecutable.");
                return;
            }

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exePath)
            };

            Process.Start(psi);
            Application.Current.Exit();
        }
        catch (Win32Exception)
        {
            ViewModel.DriveDiagnostics.Add("Elevacion cancelada por el usuario.");
        }
        catch (Exception ex)
        {
            ViewModel.DriveDiagnostics.Add($"Fallo al reintentar como admin: {ex.Message}");
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RelaunchPackagedAsAdmin()
    {
        var familyName = Windows.ApplicationModel.Package.Current.Id.FamilyName;
        var appUserModelId = $"{familyName}!App";
        var command = $"Start-Process 'shell:AppsFolder\\{appUserModelId}'";

        var psi = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(psi);
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
