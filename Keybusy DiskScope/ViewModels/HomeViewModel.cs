using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Keybusy_DiskScope.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IDriveInfoService _driveInfoService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<HomeViewModel> _logger;

    public ObservableCollection<DriveCardViewModel> Drives { get; } = new();
    public ObservableCollection<string> DriveDiagnostics { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<Models.DriveModel> AnalyzeCommand { get; }

    public HomeViewModel(
        IDriveInfoService driveInfoService,
        INavigationService navigationService,
        ILogger<HomeViewModel> logger)
    {
        _driveInfoService = driveInfoService;
        _navigationService = navigationService;
        _logger = logger;

        RefreshCommand = new AsyncRelayCommand(LoadDrivesAsync);
        AnalyzeCommand = new AsyncRelayCommand<Models.DriveModel>(AnalyzeAsync);

        _ = LoadDrivesAsync();
    }

    private async Task LoadDrivesAsync()
    {
        try
        {
            var result = await _driveInfoService.GetDrivesAsync(CancellationToken.None);
            var data = result.Drives;
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

            Drives.Clear();
            foreach (var drive in data.OrderBy(d => d.RootPath))
            {
                var displayName = BuildDisplayName(drive);
                var usedDisplay = DiskNode.FormatSize(drive.UsedBytes);
                var totalDisplay = DiskNode.FormatSize(drive.TotalBytes);
                var freeDisplay = $"{DiskNode.FormatSize(drive.FreeBytes)} Libres";
                var usedPercent = drive.TotalBytes > 0
                    ? (int)Math.Round(drive.UsedBytes * 100d / drive.TotalBytes)
                    : 0;

                var model = new Models.DriveModel
                {
                    RootPath = drive.RootPath,
                    DisplayName = displayName,
                    TypeLabel = MapTypeLabel(drive, systemRoot),
                    SubTitle = BuildSubtitle(drive),
                    UsedDisplay = usedDisplay,
                    TotalDisplay = totalDisplay,
                    FreeDisplay = freeDisplay,
                    UsedPercentage = usedPercent,
                    TemperatureDisplay = drive.TemperatureC.HasValue
                        ? $"{drive.TemperatureC.Value:0}°C"
                        : "N/D",
                    HealthStatus = string.IsNullOrWhiteSpace(drive.HealthStatus)
                        ? "N/D"
                        : drive.HealthStatus
                };

                Drives.Add(new DriveCardViewModel(model, AnalyzeCommand));
            }

            UpdateDiagnostics(result.Diagnostics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load system drives.");
            UpdateDiagnostics(new[] { "Error al cargar unidades. Ver log de depuracion." });
        }
    }

    private void UpdateDiagnostics(IEnumerable<string> diagnostics)
    {
        DriveDiagnostics.Clear();
        foreach (var line in diagnostics)
        {
            DriveDiagnostics.Add(line);
        }
    }

    private static string BuildDisplayName(DriveInfoData drive)
    {
        var root = drive.RootPath.TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(drive.VolumeLabel))
        {
            return $"Disco ({root})";
        }

        return $"{drive.VolumeLabel} ({root})";
    }

    private static string BuildSubtitle(DriveInfoData drive)
    {
        var media = string.IsNullOrWhiteSpace(drive.MediaType) ? "Disco" : drive.MediaType;
        var model = string.IsNullOrWhiteSpace(drive.Model) ? null : drive.Model;
        var format = string.IsNullOrWhiteSpace(drive.FileSystem) ? null : drive.FileSystem;

        if (model is not null && format is not null)
        {
            return $"{media} {model} - {format}";
        }

        if (model is not null)
        {
            return $"{media} {model}";
        }

        if (format is not null)
        {
            return $"{media} - {format}";
        }

        return media;
    }

    private static string MapTypeLabel(DriveInfoData drive, string? systemRoot)
    {
        var root = drive.RootPath;
        if (!string.IsNullOrWhiteSpace(systemRoot)
            && string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase))
        {
            return "SISTEMA";
        }

        return drive.DriveType switch
        {
            DriveType.Removable => "REMOVIBLE",
            DriveType.Network => "RED",
            DriveType.CDRom => "CD/DVD",
            _ => "ALMACENAMIENTO"
        };
    }

    private Task AnalyzeAsync(Models.DriveModel? drive)
    {
        if (drive is null)
        {
            DriveDiagnostics.Add("No se pudo iniciar el analisis: unidad no valida.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(drive.RootPath))
        {
            DriveDiagnostics.Add("No se pudo iniciar el analisis: ruta de unidad vacia.");
            return Task.CompletedTask;
        }

        try
        {
            _navigationService.NavigateTo("ScanPage", drive.RootPath);
        }
        catch (ArgumentException ex)
        {
            DriveDiagnostics.Add($"Navegacion invalida: {ex.Message}");
            _logger.LogError(ex, "Navigation arg error for {Drive}", drive.RootPath);
        }
        catch (InvalidOperationException ex)
        {
            DriveDiagnostics.Add($"NavigationService sin marco: {ex.Message}");
            _logger.LogError(ex, "Navigation service not ready for {Drive}", drive.RootPath);
        }
        catch (Exception ex)
        {
            DriveDiagnostics.Add($"No se pudo abrir ScanPage: {ex.Message}");
            _logger.LogError(ex, "Unexpected navigation failure for {Drive}", drive.RootPath);
        }

        return Task.CompletedTask;
    }
}
