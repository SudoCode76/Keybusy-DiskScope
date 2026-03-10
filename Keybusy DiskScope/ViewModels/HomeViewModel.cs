using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Keybusy_DiskScope.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    public ObservableCollection<Models.DriveModel> Drives { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<Models.DriveModel> AnalyzeCommand { get; }

    public HomeViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadMockAsync);
        AnalyzeCommand = new AsyncRelayCommand<Models.DriveModel>(AnalyzeAsync);

        // seed mock data for now
        _ = LoadMockAsync();
    }

    private Task LoadMockAsync()
    {
        Drives.Clear();
        Drives.Add(new Models.DriveModel
        {
            DisplayName = "Disco Local (C:)",
            TypeLabel = "SISTEMA",
            SubTitle = "SSD NVMe Samsung 980 Pro - NTFS",
            UsedDisplay = "650 GB",
            TotalDisplay = "1 TB",
            FreeDisplay = "350 GB Libres",
            UsedPercentage = 65,
            TemperatureDisplay = "42°C",
            HealthStatus = "Bueno (98%)"
        });

        Drives.Add(new Models.DriveModel
        {
            DisplayName = "Datos (D:)",
            TypeLabel = "ALMACENAMIENTO",
            SubTitle = "HDD Seagate Barracuda - NTFS",
            UsedDisplay = "1.8 TB",
            TotalDisplay = "4 TB",
            FreeDisplay = "2.2 TB Libres",
            UsedPercentage = 45,
            TemperatureDisplay = "36°C",
            HealthStatus = "Bueno"
        });

        return Task.CompletedTask;
    }

    private Task AnalyzeAsync(Models.DriveModel? drive)
    {
        if (drive is null)
        {
            return Task.CompletedTask;
        }

        // placeholder - actual scan logic lives in ScanService and ScanViewModel
        return Task.CompletedTask;
    }
}
