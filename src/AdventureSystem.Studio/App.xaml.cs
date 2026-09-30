using AdventureSystem.Studio.Pages;
using AdventureSystem.Studio.Services;

namespace AdventureSystem.Studio;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var studio = new StudioPage(StudioDocument.CreateNew());
		Page page = studio;
		// Windows shows MenuBarItems only inside a NavigationPage (in its bar), so host the page in one there.
		if (DeviceInfo.Platform == DevicePlatform.WinUI)
			page = new NavigationPage(page) { BarBackgroundColor = AdventureSystem.Maui.Theme.Sidebar, BarTextColor = AdventureSystem.Maui.Theme.Text };
		// The iPad: commands live in the navigation bar.
		if (DeviceInfo.Platform == DevicePlatform.iOS)
			page = new NavigationPage(page) { BarBackgroundColor = AdventureSystem.Maui.Theme.Window, BarTextColor = AdventureSystem.Maui.Theme.Text };
		var window = new Window(page)
		{
			Title = "Adventure System Studio",
		};
#if IOS
		// Open on the Studio with the last adventure (unless a development demo was asked for), and save whenever the app is put away.
		bool demo = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AC_STUDIO_DEMO")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AC_STUDIO_DEMO_FILE"));
		bool shown = false;
		window.Activated += (_, _) =>
		{
			if (shown || demo) return;
			shown = true;
#if DEBUG
			// Development aid: AC_STUDIO_OPEN=<path> opens a file as if it had been picked in the browser.
			if (Environment.GetEnvironmentVariable("AC_STUDIO_OPEN") is { Length: > 0 } open)
			{
				DocumentBrowser.OpenFile(studio, open);
				if (Environment.GetEnvironmentVariable("AC_STUDIO_EDIT") is { Length: > 0 } title)
					studio.Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(2), () => studio.DebugRetitle(title));
				return;
			}
#endif
			DocumentBrowser.StartOnStudio(studio);
		};
		window.Deactivated += (_, _) => studio.SaveNow();
		window.Stopped += (_, _) => studio.SaveNow();
#endif
		if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
		{
			AdventureSystem.Maui.WindowSizing.Apply(window, 1440, 900, 1000, 640);
		}
		return window;
	}
}
