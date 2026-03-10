# AGENTS.md — Keybusy DiskScope

## Project Overview

**Keybusy DiskScope** is a WinUI 3 disk analysis application for Windows. Core features:
- Scan any selected drive/partition and display all files and folders with their size and type, in the same hierarchical order as Windows Explorer.
- Save each scan as a named snapshot (stored with date/time).
- Compare any two snapshots to identify exactly where disk usage grew (not just the latest scan, but any historical pair).

**Stack:** WinUI 3 / Windows App SDK · .NET 8 · Single-Project MSIX packaging · Mica backdrop · MVVM (CommunityToolkit.Mvvm) · DI (Microsoft.Extensions.DependencyInjection)

---

## Build Commands

No solution file exists. Build directly against the `.csproj`.

```powershell
# Debug build (x64)
dotnet build "Keybusy DiskScope.csproj" -c Debug -p:Platform=x64

# Release build (x64)
dotnet build "Keybusy DiskScope.csproj" -c Release -p:Platform=x64

# Other platforms
dotnet build "Keybusy DiskScope.csproj" -c Debug -p:Platform=x86
dotnet build "Keybusy DiskScope.csproj" -c Debug -p:Platform=ARM64

# Publish self-contained (trimmed, x64)
dotnet publish "Keybusy DiskScope.csproj" -p:PublishProfile=win-x64.pubxml -c Release
```

**Target framework:** `net8.0-windows10.0.19041.0`  
**Min Windows version:** 10.0.17763.0 (Windows 10 1809)  
**Nullable:** `enable` — all reference types must be annotated.

## Test Commands

No test project exists yet. When added, use:

```powershell
# Run all tests
dotnet test

# Run a single test by fully-qualified name
dotnet test --filter "FullyQualifiedName=Keybusy_DiskScope.Tests.ScanServiceTests.ScanReturnsCorrectTotalSize"

# Run all tests in one class
dotnet test --filter "ClassName=Keybusy_DiskScope.Tests.ScanServiceTests"
```

ViewModels must not reference `Microsoft.UI.Xaml.*` — this keeps them fully unit-testable without a UI host.

---

## Project Structure

```
Keybusy DiskScope/
├── App.xaml / App.xaml.cs          — Entry point, DI container, service registration
├── MainWindow.xaml/.cs             — Shell window (MicaBackdrop)
├── Views/                          — Pages (one per NavigationView item)
├── ViewModels/                     — CommunityToolkit.Mvvm ViewModels
├── Services/                       — INavigationService, IScanService, ISnapshotService, etc.
├── Models/                         — Plain C# data models (no UI dependencies)
├── Converters/                     — IValueConverter implementations
├── Controls/                       — Reusable UserControls
├── Assets/                         — App icons
└── Properties/                     — launchSettings.json, PublishProfiles/
```

---

## Non-Negotiable WinUI 3 Rules

These rules are enforced by the `.agents/skills/winui3-full-skill` skill and apply to every file.

1. **Always use `x:Bind`** (compiled binding). Never use `{Binding}` unless reflection is required.
2. **Always set `x:DataType`** on every `DataTemplate`.
3. **ViewModels must never reference `Microsoft.UI.Xaml.*`** types.
4. **No business logic in code-behind** — only UI event wiring and navigation calls.
5. **Use `NavigationView`** for all primary app navigation.
6. **No `.Result`, `.Wait()`, or `Thread.Sleep`** on the UI thread.
7. **Always provide `AutomationProperties.Name`** on every interactive control.

---

## Code Style

### Namespaces
- Root namespace: `Keybusy_DiskScope` (underscores replace spaces in C# identifiers).
- Use **file-scoped namespaces** (C# 10+): `namespace Keybusy_DiskScope.ViewModels;`
- Group `using` statements: `System.*` → third-party → project-local. One blank line between groups.
- Hoist common imports into `GlobalUsings.cs`.

### Naming Conventions
| Element | Convention | Example |
|---|---|---|
| Classes, methods, properties, events | `PascalCase` | `ScanService`, `StartScanAsync` |
| Private fields | `_camelCase` | `private bool _isScanning;` |
| Async methods | `PascalCase` + `Async` suffix | `LoadSnapshotsAsync` |
| Observable properties (source gen) | `_camelCase` field + `[ObservableProperty]` | `private long _totalSize;` |
| Interfaces | `I` prefix | `IScanService` |
| Pages / Views | `Page` suffix | `ScanPage`, `ComparePage` |
| ViewModels | `ViewModel` suffix | `ScanViewModel`, `CompareViewModel` |

### `var` Usage
- Use `var` when the type is obvious from the right-hand side.
- Avoid `var` for primitive types (`int`, `long`, `bool`, `string`).

### Nullable Reference Types
- Project has `<Nullable>enable</Nullable>`. Annotate all nullable references: `string?`, `Window?`.
- Do not suppress nullable warnings with `!` unless you have verified the value is non-null.

---

## MVVM Pattern

```csharp
// ViewModel — CommunityToolkit.Mvvm source generators
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Keybusy_DiskScope.ViewModels;

public partial class ScanViewModel : ObservableObject
{
    private readonly IScanService _scanService;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private long _totalSize;
    [ObservableProperty] private string? _errorMessage;

    public ScanViewModel(IScanService scanService)
        => _scanService = scanService;

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task StartScanAsync(CancellationToken ct)
    {
        IsScanning = true;
        ErrorMessage = null;
        try { TotalSize = await _scanService.ScanAsync(SelectedDrive, ct); }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsScanning = false; }
    }
}
```

- Resolve ViewModels from DI in the View: `ViewModel = App.Services.GetRequiredService<ScanViewModel>();`
- Start async data loading in the `Loaded` event handler, never in the constructor.

---

## Async & Threading

- All async work via `async`/`await`. Never block the UI thread.
- Pass `CancellationToken` through the entire call chain.
- Use `DispatcherQueue.TryEnqueue(...)` to marshal results back to the UI thread from background threads.
- `async void` is only acceptable in code-behind event handlers — always wrap in `try/catch`.
- File system scans **must run on a background thread**: `await Task.Run(() => ..., ct)`.

---

## XAML Patterns

### Data Binding
```xaml
<!-- CORRECT — compiled, type-safe -->
<TextBlock Text="{x:Bind ViewModel.TotalSize, Mode=OneWay}" />

<!-- FORBIDDEN — runtime reflection -->
<TextBlock Text="{Binding TotalSize}" />
```

`x:Bind` default mode is `OneTime`. Explicitly add `Mode=OneWay` or `Mode=TwoWay` for live data.

### DataTemplate (x:DataType required)
```xaml
<TreeView.ItemTemplate>
    <DataTemplate x:DataType="models:DiskNode">
        <TreeViewItem ItemsSource="{x:Bind Children}"
                      Content="{x:Bind DisplayName}" />
    </DataTemplate>
</TreeView.ItemTemplate>
```

### Theming — always use ThemeResource
```xaml
<!-- CORRECT -->
<Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
<!-- FORBIDDEN -->
<Border Background="#FF1E1E1E" />
```

### Status / Error UI pattern
```xaml
<InfoBar IsOpen="{x:Bind ViewModel.HasError, Mode=OneWay}"
         Severity="Error"
         Title="Scan failed"
         Message="{x:Bind ViewModel.ErrorMessage, Mode=OneWay}"
         IsClosable="True" />
<ProgressRing IsActive="{x:Bind ViewModel.IsScanning, Mode=OneWay}"
              HorizontalAlignment="Center" VerticalAlignment="Center" />
```

### Accessibility (mandatory)
```xaml
<Button AutomationProperties.Name="Start disk scan">
    <SymbolIcon Symbol="Play" />
</Button>
```

---

## Agent Skill Reference

The `.agents/skills/winui3-full-skill/` directory contains a comprehensive WinUI 3 skill:

| Resource | Purpose |
|---|---|
| `catalog/best-practices.md` | Authoritative rules (perf, threading, accessibility, MVVM, DI) |
| `catalog/controls.md` | Index of all ~70 WinUI 3 controls with snippet links |
| `catalog/patterns.md` | MVVM, DI, navigation, async, error-handling patterns |
| `snippets/patterns/app-shell.md` | NavigationView shell template |
| `snippets/patterns/mvvm-di-setup.md` | DI container wiring in App.xaml.cs |
| `snippets/patterns/theming.md` | Mica, light/dark, ThemeResource usage |
| `snippets/collections/treeview.md` | TreeView (needed for folder hierarchy) |

**Always consult the skill before implementing a new control or pattern.**
