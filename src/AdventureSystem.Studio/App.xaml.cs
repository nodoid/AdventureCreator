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
		Page page = new StudioPage(StudioDocument.CreateNew());
		// Windows shows MenuBarItems only inside a NavigationPage (in its bar), so host the page in one there.
		if (DeviceInfo.Platform == DevicePlatform.WinUI)
			page = new NavigationPage(page) { BarBackgroundColor = AdventureSystem.Maui.Theme.Sidebar, BarTextColor = AdventureSystem.Maui.Theme.Text };
		var window = new Window(page)
		{
			Title = "Adventure System Studio",
		};
		if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
		{
			AdventureSystem.Maui.WindowSizing.Apply(window, 1440, 900, 1000, 640);
		}
		return window;
	}
}
