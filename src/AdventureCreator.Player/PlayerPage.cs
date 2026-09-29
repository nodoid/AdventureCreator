using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;
using AdventureCreator.Core.Samples;
using AdventureCreator.Maui;

namespace AdventureCreator.Player;

/// <summary>
/// The standalone game player. Plays the game embedded in the app (exported from the Studio); if there is none it
/// offers to open a .adventure file or play the example game.
/// </summary>
public sealed class PlayerPage : ContentPage
{
#if DEBUG
	private const bool IsDevelopmentBuild = true;
#else
	private const bool IsDevelopmentBuild = false;
#endif
	private readonly GamePlayerView player = new();
	private readonly VerticalStackLayout chooser;
	private bool loaded;
	private bool embedded;

	public PlayerPage()
	{
		Title = "Adventure Player";
		BackgroundColor = Color.FromArgb(Theme.GameBackground);
		player.ShowShortcuts = DeviceInfo.Idiom == DeviceIdiom.Phone;
		player.PictureHeightFraction = DeviceInfo.Idiom == DeviceIdiom.Phone ? 0.34 : 0.45;
		player.QuitRequested += (_, _) => Quit();
		player.IsVisible = false;

		var open = new Button { Text = "Open a game…" };
		open.Clicked += async (_, _) => await OpenGameAsync();
		// The Doctor Who fan adventure is for development builds only, never store releases.
		var example = new Button { Text = "Play “Genesis” (example)", IsVisible = IsDevelopmentBuild };
		example.Clicked += (_, _) => Play(ExampleAdventures.Genesis());
		var example2 = new Button { Text = "Play “The Lighthouse” (example)" };
		example2.Clicked += (_, _) => Play(ExampleAdventures.Lighthouse());
		chooser = new VerticalStackLayout
		{
			Spacing = 16,
			Padding = 40,
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			Children =
			{
				new Image { Source = "splash_logo.png", HeightRequest = 140 },
				new Label { Text = "Adventure Player", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Theme.Accent, HorizontalTextAlignment = TextAlignment.Center },
				new Label { Text = "Play text and graphic adventures made with Adventure Creator Studio.", TextColor = Theme.Text, HorizontalTextAlignment = TextAlignment.Center },
				open,
				example,
				example2,
			},
		};

		Content = new Grid { Children = { player, chooser } };
		// Desktop windows already show the title in the title bar.
		NavigationPage.SetHasNavigationBar(this, DeviceInfo.Idiom != DeviceIdiom.Desktop);
		BuildMenus();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (loaded) return;
		loaded = true;
		var game = await LoadEmbeddedAsync();
		if (game != null)
		{
			embedded = true;
			Play(game);
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		player.Stop();
	}

	private static async Task<Adventure?> LoadEmbeddedAsync()
	{
		try
		{
			using var s = await FileSystem.OpenAppPackageFileAsync(StandaloneExporter.GameFileName);
			using var ms = new MemoryStream();
			await s.CopyToAsync(ms);
			return AdventurePackage.Load(ms.ToArray());
		}
		catch
		{
			// Not packaged as an app asset: try next to the executable / appended payload (desktop templates).
			return StandaloneExporter.LocateEmbeddedGame();
		}
	}

	private void Play(Adventure game)
	{
		chooser.IsVisible = false;
		player.IsVisible = true;
		Title = game.Title;
		if (Window != null) Window.Title = game.Title;
		var saveDir = Path.Combine(FileSystem.AppDataDirectory, "Saves", StandaloneExporter.SafeFileName(game.Title));
		player.Load(game, new FileSaveStorage(saveDir));
		Dispatcher.Dispatch(async () =>
		{
			await player.ShowTitleScreenAsync();
			await player.OfferContinueAsync();
		});
	}

	private async Task OpenGameAsync()
	{
		try
		{
			var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Open an adventure" });
			if (result == null) return;
			await using var stream = await result.OpenReadAsync();
			using var ms = new MemoryStream();
			await stream.CopyToAsync(ms);
			Play(result.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
				? AdventurePackage.FromJson(System.Text.Encoding.UTF8.GetString(ms.ToArray()))
				: AdventurePackage.Load(ms.ToArray()));
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Could not open game", ex.Message, "OK");
		}
	}

	/// <summary>Called after the player confirmed QUIT (the position has been autosaved): end the process.</summary>
	private void Quit()
	{
		player.Stop();
		Environment.Exit(0);
	}

	private void BuildMenus()
	{
		MenuFlyoutItem Item(string text, Action action, string? key = null, KeyboardAcceleratorModifiers mods = KeyboardAcceleratorModifiers.Cmd)
		{
			var item = new MenuFlyoutItem { Text = text };
			item.Clicked += (_, _) => action();
			if (key != null) item.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = key, Modifiers = mods });
			return item;
		}

		// Shortcuts must not collide with macOS system menu items (⌘Z Undo, ⌘I Italic…), or UIKit drops the whole menu.
		var file = new MenuBarItem { Text = "Game" };
		if (!embedded) file.Add(Item("Open Game…", () => _ = OpenGameAsync(), "O"));
		file.Add(Item("Save Game…", () => _ = player.ShowSaveDialogAsync(), "S"));
		file.Add(Item("Load Game…", () => _ = player.ShowLoadDialogAsync(), "L", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Alt));
		file.Add(Item("Restart", () => _ = player.SubmitAsync("restart"), "R", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Shift));
		file.Add(Item("Undo Move", () => _ = player.SubmitAsync("undo"), "Z", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Alt));
		file.Add(new MenuFlyoutSeparator());
		file.Add(Item("Quit Game…", () => _ = player.ConfirmQuitAsync()));

		var commands = new MenuBarItem { Text = "Commands" };
		commands.Add(Item("Previous Command", player.RecallPrevious, "Up", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Alt));
		commands.Add(Item("Next Command", player.RecallNext, "Down", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Alt));
		commands.Add(new MenuFlyoutSeparator());
		commands.Add(Item("Look", () => _ = player.SubmitAsync("look"), "L", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Shift));
		commands.Add(Item("Inventory", () => _ = player.SubmitAsync("inventory"), "I", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Shift));
		commands.Add(Item("Hint", () => _ = player.SubmitAsync("hint"), "H", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Shift));
		commands.Add(Item("Score", () => _ = player.SubmitAsync("score")));

		var sound = new MenuBarItem { Text = "Sound" };
		MenuFlyoutItem? mute = null;
		mute = Item("Mute", () =>
		{
			player.Audio.Muted = !player.Audio.Muted;
			if (player.Audio.Muted) player.Audio.StopAll();
			mute!.Text = player.Audio.Muted ? "Unmute" : "Mute";
		}, "M", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Shift);
		sound.Add(mute);

		MenuBarItems.Add(file);
		MenuBarItems.Add(commands);
		MenuBarItems.Add(sound);
	}
}
