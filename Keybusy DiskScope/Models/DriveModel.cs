namespace Keybusy_DiskScope.Models;

using System.ComponentModel;

public class DriveModel : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set { if (value != _displayName) { _displayName = value; OnPropertyChanged(nameof(DisplayName)); } }
    }

    private string _typeLabel = string.Empty;
    public string TypeLabel
    {
        get => _typeLabel;
        set { if (value != _typeLabel) { _typeLabel = value; OnPropertyChanged(nameof(TypeLabel)); } }
    }

    private string _subTitle = string.Empty;
    public string SubTitle
    {
        get => _subTitle;
        set { if (value != _subTitle) { _subTitle = value; OnPropertyChanged(nameof(SubTitle)); } }
    }

    private string _usedDisplay = string.Empty;
    public string UsedDisplay
    {
        get => _usedDisplay;
        set { if (value != _usedDisplay) { _usedDisplay = value; OnPropertyChanged(nameof(UsedDisplay)); } }
    }

    private string _totalDisplay = string.Empty;
    public string TotalDisplay
    {
        get => _totalDisplay;
        set { if (value != _totalDisplay) { _totalDisplay = value; OnPropertyChanged(nameof(TotalDisplay)); } }
    }

    private string _freeDisplay = string.Empty;
    public string FreeDisplay
    {
        get => _freeDisplay;
        set { if (value != _freeDisplay) { _freeDisplay = value; OnPropertyChanged(nameof(FreeDisplay)); } }
    }

    private int _usedPercentage;
    public int UsedPercentage
    {
        get => _usedPercentage;
        set { if (value != _usedPercentage) { _usedPercentage = value; OnPropertyChanged(nameof(UsedPercentage)); } }
    }

    private string _temperatureDisplay = string.Empty;
    public string TemperatureDisplay
    {
        get => _temperatureDisplay;
        set { if (value != _temperatureDisplay) { _temperatureDisplay = value; OnPropertyChanged(nameof(TemperatureDisplay)); } }
    }

    private string _healthStatus = string.Empty;
    public string HealthStatus
    {
        get => _healthStatus;
        set { if (value != _healthStatus) { _healthStatus = value; OnPropertyChanged(nameof(HealthStatus)); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
