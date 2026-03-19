using CommunityToolkit.Mvvm.Input;

namespace Keybusy_DiskScope.ViewModels;

public sealed class DriveCardViewModel
{
    public DriveCardViewModel(Models.DriveModel drive, IAsyncRelayCommand<Models.DriveModel> analyzeCommand)
    {
        Drive = drive;
        AnalyzeCommand = analyzeCommand;
    }

    public Models.DriveModel Drive { get; }

    public IAsyncRelayCommand<Models.DriveModel> AnalyzeCommand { get; }
}
