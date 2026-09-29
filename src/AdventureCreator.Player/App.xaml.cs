namespace AdventureCreator.Player;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new NavigationPage(new PlayerPage()) { BarBackgroundColor = Color.FromArgb("#0E1420"), BarTextColor = Colors.WhiteSmoke })
		{
			Title = AppInfo.Current.Name,
		};
		if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
		{
			window.Width = 900;
			window.Height = 820;
			window.MinimumWidth = 480;
			window.MinimumHeight = 500;
		}
		return window;
	}
}
