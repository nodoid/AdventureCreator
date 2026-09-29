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
		var window = new Window(new StudioPage(StudioDocument.CreateNew()))
		{
			Title = "Adventure Creator Studio",
		};
		if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
		{
			AdventureCreator.Maui.WindowSizing.Apply(window, 1440, 900);
			window.MinimumWidth = 1000;
			window.MinimumHeight = 640;
		}
		return window;
	}
}
