using System.ComponentModel;
using System.Diagnostics;
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
            if (IsPackaged())
            {
                ViewModel.DriveDiagnostics.Add("La app empaquetada no puede elevarse. Ejecuta la version unpackaged o inicia Visual Studio como administrador.");
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
                Verb = "runas"
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
}
