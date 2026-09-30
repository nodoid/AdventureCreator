using AdventureSystem.Studio.Pages;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace AdventureSystem.Studio;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	/// <summary>The Studio's commands in the iPad menu bar and the ⌘ shortcut list (with a hardware keyboard).</summary>
	public override void BuildMenu(IUIMenuBuilder builder)
	{
		base.BuildMenu(builder);
		if (builder.System != UIMenuSystem.MainSystem) return;

		var action = new Selector("studioCommand:");
		UIKeyCommand Key(string title, string id, string input, UIKeyModifierFlags mods = UIKeyModifierFlags.Command) =>
			UIKeyCommand.Create(new NSString(title), null, action, new NSString(input), mods, new NSString(id));
		UICommand Plain(string title, string id) => UICommand.Create(title, null, action, new NSString(id));
		UIMenu Inline(params UIMenuElement[] children) => UIMenu.Create("", null, UIMenuIdentifier.None, UIMenuOptions.DisplayInline, children);
		const UIKeyModifierFlags Cmd = UIKeyModifierFlags.Command, Alt = UIKeyModifierFlags.Alternate, Shift = UIKeyModifierFlags.Shift;

		builder.InsertChildMenuAtStart(Inline(
			Key("Import Game…", "import", "i", Cmd | Shift),
			Key("Export Game File…", "export", "x", Cmd | Shift),
			Plain("Send a Copy…", "send")), UIMenuIdentifier.File.GetConstant());

		builder.InsertChildMenuAtEnd(Inline(
			Key("New Room", "new:Rooms", "r", Cmd | Alt),
			Key("New Item", "new:Items", "i", Cmd | Alt),
			Key("New Trigger", "new:Triggers", "t", Cmd | Alt),
			Key("New Puzzle", "new:Puzzles", "p", Cmd | Alt),
			Key("New Random Event", "new:Events", "e", Cmd | Alt),
			Plain("New Picture", "new:Pictures"),
			Plain("New Sound…", "new:Sounds"),
			Plain("New Command", "new:Commands"),
			Key("Duplicate Selected", "duplicate", "d"),
			Key("Delete Selected…", "delete", "\b")), UIMenuIdentifier.Edit.GetConstant());

		var sections = Enum.GetValues<Section>().Select((s, i) => (UIMenuElement)(i < 9
			? Key(StudioPage.SectionName(s), "section:" + s, (i + 1).ToString())
			: Plain(StudioPage.SectionName(s), "section:" + s))).ToList();
		builder.InsertChildMenuAtStart(Inline(sections.ToArray()), UIMenuIdentifier.View.GetConstant());

		var adventure = UIMenu.Create("Adventure", null, UIMenuIdentifier.None, 0, new UIMenuElement[]
		{
			Key("Test Play", "show:TestPlay", "r"),
			Key("Validate", "validate", "k"),
			Key("Show Map", "show:Map", "m", Cmd | Shift),
			Inline(
				Key("Go to Command Line", "commandline", "l"),
				Key("Previous Command", "previous", UIKeyCommand.UpArrow, Cmd | Alt),
				Key("Next Command", "next", UIKeyCommand.DownArrow, Cmd | Alt),
				Key("Run Commands…", "runcommands", "r", Cmd | Shift),
				Key("Save Position…", "savepos", "s", Cmd | Alt),
				Key("Load Position…", "loadpos", "l", Cmd | Alt),
				Key("Show or Hide Watch", "watch", "0", Cmd | Alt)),
		});
		builder.InsertSiblingMenuAfter(adventure, UIMenuIdentifier.View.GetConstant());

		builder.InsertChildMenuAtStart(Inline(Key("User Guide…", "guide", "?", Cmd | Shift)), UIMenuIdentifier.Help.GetConstant());
	}

	[Export("studioCommand:")]
	public void StudioCommand(UICommand sender)
	{
		if (sender.PropertyList is NSString id) StudioPage.Current?.RunCommand(id.ToString());
	}
}
