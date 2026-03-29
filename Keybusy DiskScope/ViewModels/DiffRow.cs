using CommunityToolkit.Mvvm.ComponentModel;

using Keybusy_DiskScope.Models;

namespace Keybusy_DiskScope.ViewModels;

public partial class DiffRow : ObservableObject
{
    public DiffRow(DiffNode node, int depth)
    {
        Node = node;
        Depth = depth;
    }

    public DiffNode Node { get; }
    public int Depth { get; }

    public string Name => Node.Name;
    public bool IsDirectory => Node.IsDirectory;
    public bool IsFile => !Node.IsDirectory;
    public bool HasChildren => Node.Children.Count > 0;
    public bool IsExpanded
    {
        get => Node.IsExpanded;
        set
        {
            if (Node.IsExpanded == value)
            {
                return;
            }

            Node.IsExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }
    public string ExpandGlyph => IsExpanded ? "\uE70D" : "\uE76C";
    public DiffStatus Status => Node.Status;
    public long SizeBefore => Node.SizeBefore;
    public long SizeAfter => Node.SizeAfter;
    public long SizeDelta => Node.SizeDelta;

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayPreviousSize => DiskNode.FormatSize(SizeBefore);
    public string DisplayCurrentSize => DiskNode.FormatSize(SizeAfter);
    public string DisplayDelta => Node.DisplayDelta;
    public string StatusLabel => Status switch
    {
        DiffStatus.Added => "Nuevo",
        DiffStatus.Removed => "Eliminado",
        DiffStatus.Grown => "Modificado",
        DiffStatus.Shrunk => "Modificado",
        _ => "Sin cambios"
    };

    public bool CanDelete => Status != DiffStatus.Removed;
}
