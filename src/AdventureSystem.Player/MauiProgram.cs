using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace AdventureSystem.Player;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.AddAudio()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if MACCATALYST
		// In the Mac idiom UIKit draws buttons as native macOS push buttons, which ignore the Player's colours and
		// look disabled; the iPad behavioural style keeps the accent background and white text.
		Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping("PadStyle", (handler, _) =>
			handler.PlatformView.PreferredBehavioralStyle = UIKit.UIBehavioralStyle.Pad);
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
