using AdventureCreator.Studio.Pages;
using AdventureCreator.Studio.Services;

namespace AdventureCreator.Studio;

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
			page = new NavigationPage(page) { BarBackgroundColor = AdventureCreator.Maui.Theme.Sidebar, BarTextColor = AdventureCreator.Maui.Theme.Text };
		var window = new Window(page)
		{
			Title = "Adventure Creator Studio",
		};
		if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
		{
			AdventureCreator.Maui.WindowSizing.Apply(window, 1440, 900, 1000, 640);
		}
		return window;
	}
}
