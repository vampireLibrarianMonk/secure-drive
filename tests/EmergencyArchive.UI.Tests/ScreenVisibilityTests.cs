using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using EmergencyArchive.UI;
using Xunit;

namespace EmergencyArchive.UI.Tests;

/// <summary>
/// Renders the REAL MainWindow headlessly and asserts that exactly one of the
/// four screens is visible in each application state. These tests guard against
/// screen-layering regressions — for example a misdirected <c>IsVisible</c>
/// binding (a binding evaluated against the wrong DataContext silently defaults
/// a control to visible), which once left Setup Mode stacked over the password
/// screen.
/// </summary>
public class ScreenVisibilityTests
{
    private const string Create = "CreateScreen";
    private const string Password = "PasswordScreen";
    private const string Browse = "BrowseScreen";
    private const string Setup = "SetupScreen";

    private static readonly string[] AllScreens = [Create, Password, Browse, Setup];

    private static (MainWindow Window, MainWindowViewModel Vm) ShowWindow()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm);
    }

    /// <summary>Finds a named screen container and returns whether it is effectively visible.</summary>
    private static bool ScreenVisible(MainWindow window, string name)
    {
        Control control = window.GetLogicalDescendants()
            .OfType<Control>()
            .Single(c => c.Name == name);
        return control.IsVisible;
    }

    private static IReadOnlyList<string> VisibleScreens(MainWindow window) =>
        AllScreens.Where(s => ScreenVisible(window, s)).ToList();

    [AvaloniaFact]
    public void FirstRun_ShowsOnlyCreateScreen()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowWindow();
        vm.IsFirstRun = true;
        vm.IsUnlocked = false;
        vm.IsSetupMode = false;

        Assert.Equal([Create], VisibleScreens(window));
    }

    [AvaloniaFact]
    public void LockedWithExistingVault_ShowsOnlyPasswordScreen()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowWindow();
        vm.IsFirstRun = false;
        vm.IsUnlocked = false;
        vm.IsSetupMode = false;

        Assert.Equal([Password], VisibleScreens(window));
    }

    [AvaloniaFact]
    public void Unlocked_ShowsOnlyBrowseScreen()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowWindow();
        vm.IsFirstRun = false;
        vm.IsUnlocked = true;
        vm.IsSetupMode = false;

        Assert.Equal([Browse], VisibleScreens(window));
    }

    [AvaloniaFact]
    public void SetupMode_ShowsOnlySetupScreen()
    {
        // Regression guard: this is the exact state where the estate-planning
        // card (name/contact fields) once overlapped the password screen.
        (MainWindow window, MainWindowViewModel vm) = ShowWindow();
        vm.IsFirstRun = false;
        vm.IsUnlocked = true;
        vm.IsSetupMode = true;

        Assert.Equal([Setup], VisibleScreens(window));
    }

    [AvaloniaTheory]
    [InlineData(true, false, false)]   // first run
    [InlineData(false, false, false)]  // locked, vault exists
    [InlineData(false, true, false)]   // unlocked, browsing
    [InlineData(false, true, true)]    // unlocked, setup mode
    public void ExactlyOneScreenIsVisible_InEveryReachableState(bool firstRun, bool unlocked, bool setupMode)
    {
        (MainWindow window, MainWindowViewModel vm) = ShowWindow();
        vm.IsFirstRun = firstRun;
        vm.IsUnlocked = unlocked;
        vm.IsSetupMode = setupMode;

        Assert.Single(VisibleScreens(window));
    }
}
