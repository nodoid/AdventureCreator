namespace AdventureCreator.Player;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new NavigationPage(new PlayerPage()) { BarBackgroundColor = AdventureCreator.Maui.Theme.Sidebar, BarTextColor = AdventureCreator.Maui.Theme.Text })
		{
			Title = AppInfo.Current.Name,
		};
		if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
		{
			AdventureCreator.Maui.WindowSizing.Apply(window, 900, 820);
			window.MinimumWidth = 480;
			window.MinimumHeight = 500;
		}
		return window;
	}
}
