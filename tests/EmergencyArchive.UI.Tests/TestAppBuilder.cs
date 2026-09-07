using Avalonia;
using Avalonia.Headless;
using EmergencyArchive.UI;
using EmergencyArchive.UI.Tests;

// Wire the headless test host to the REAL application (App.axaml), so the same
// FluentTheme + AppStyles.axaml (the `card` etc. classes) are applied and the
// MainWindow renders exactly as it does at runtime. App.OnFrameworkInitialization
// only creates a MainWindow under a classic desktop lifetime, which the headless
// host does not provide — so no window is auto-created here.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace EmergencyArchive.UI.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
