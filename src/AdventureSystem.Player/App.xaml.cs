namespace AdventureSystem.Player;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
		// Android: shrink the layout above the on-screen keyboard instead of panning the whole window off-screen.
		Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.SetWindowSoftInputModeAdjust(
			this, Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new NavigationPage(new PlayerPage()) { BarBackgroundColor = AdventureSystem.Maui.Theme.Sidebar, BarTextColor = AdventureSystem.Maui.Theme.Text })
		{
			Title = AppInfo.Current.Name,
		};
		if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
		{
			AdventureSystem.Maui.WindowSizing.Apply(window, 900, 820, 480, 500);
		}
		return window;
	}
}
