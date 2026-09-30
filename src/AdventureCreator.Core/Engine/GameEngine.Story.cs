using System.Text.RegularExpressions;
using AdventureCreator.Core.ZMachine;

namespace AdventureCreator.Core.Engine;

/// <summary>
/// Z-code stories: the engine hands input to its Z-machine and turns what the story prints into the usual output
/// events, so every host (Player, Studio Test Play, console, exported apps) plays them unchanged.
/// </summary>
public sealed partial class GameEngine
{
    private readonly ZMachine.ZMachine? story;

    /// <summary>True when this game is a Z-code story run on the Z-machine.</summary>
    public bool IsStory => story != null;

    /// <summary>The player's location for save names and status: the room, or the story's status line.</summary>
    public string LocationName => story == null ? CurrentRoom?.Name ?? "" : story.Status3?.Location ?? story.StatusLine;

    private static readonly Regex HostRestore = new(@"^\s*restore\s+(?:""(?<n>[^""]+)""|(?<n>autosave))\s*$", RegexOptions.IgnoreCase);

    private TurnResult StartStory()
    {
        BeginOutput();
        State = GameState.Initial(Adventure);
        started = true;
        if (Adventure.IntroPictureId != null && Adventure.Settings.ShowPictures)
            Emit(new OutputEvent(OutputKind.Picture, Id: Adventure.IntroPictureId));
        story!.Reset();
        RunStory(null);
        EmitStatus();
        return EndOutput();
    }

    private TurnResult SubmitStory(string input)
    {
        if (!started) StartStory();
        BeginOutput();
        var restore = HostRestore.Match(input ?? "");
        if (restore.Success && !story!.HasQuit)
        {
            // The host's load dialog (and "continue where you left off") restores between turns.
            DoRestore(restore.Groups["n"].Value);
        }
        else if (story!.HasQuit)
        {
            Say("The story has ended.", TextStyle.System);
        }
        else
        {
            State.Turns++;
            RunStory(input ?? "");
        }
        EmitStatus();
        return EndOutput();
    }

    private void RunStory(string? input)
    {
        ZStop stop;
        try
        {
            stop = story!.Run(input);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Say(story!.TakeOutput());
            Say("[Z-machine error: " + ex.Message + "]", TextStyle.Error);
            return;
        }
        if (story.ClearRequested) Emit(new OutputEvent(OutputKind.ClearScreen));
        foreach (var picture in story.PicturesDrawn.Distinct())
            if (Adventure.FindPicture(ZStory.PictureId(picture)) is { } p && p.Width >= 64 && p.Height >= 48 && Adventure.Settings.ShowPictures)
                Emit(new OutputEvent(OutputKind.Picture, Id: p.Id));
        var text = CleanStoryText(story.TakeOutput());
        if (text.Length > 0) Say(text);
        if (story.BeepRequested) Emit(new OutputEvent(OutputKind.Beep));
        if (story.Status3 is { } s) State.Score = s.Time ? 0 : s.Score;

        if (story.SaveRequested)
        {
            if (HostHandlesSaveDialogs) Emit(new OutputEvent(OutputKind.SaveRequested));
            else DoSave("default");
        }
        if (story.RestoreRequested)
        {
            if (HostHandlesSaveDialogs) Emit(new OutputEvent(OutputKind.RestoreRequested));
            else DoRestore(null);
        }
        if (stop == ZStop.Quit)
        {
            State.GameOver = true;
            Emit(new OutputEvent(OutputKind.GameOver, "ended"));
            Emit(new OutputEvent(OutputKind.Quit));
        }
    }

    /// <summary>Drops the story's own prompt (the host shows one) and surplus blank lines.</summary>
    public static string CleanStoryText(string text)
    {
        text = text.Replace("\r", "");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = text.TrimEnd();
        if (text.EndsWith('>')) text = text[..^1].TrimEnd();
        return text.Trim('\n');
    }

    private void RestoreStory(SaveGame save)
    {
        try
        {
            if (State.ZState == null) throw new InvalidDataException("The saved game has no story position.");
            story!.RestoreState(Convert.FromBase64String(State.ZState));
            Say(Msg(Engine.Msg.Restored, null) + (save.Name is "default" ? "" : $" (“{(save.IsAutosave ? "autosave" : save.Name)}”)"), TextStyle.System);
            if (!string.IsNullOrWhiteSpace(story.StatusLine)) Say(story.StatusLine, TextStyle.RoomTitle);
        }
        catch (Exception ex)
        {
            Say("Restore failed: " + ex.Message, TextStyle.Error);
        }
    }

    private void EmitStoryStatus()
    {
        var s = story!;
        // "location|score|max|turns": V3 stories report score and moves; later ones only have a status line.
        string status = s.Status3 is { } v3
            ? v3.Time ? $"{v3.Location}    {v3.Score}:{v3.Turns:00}" : $"{v3.Location}|{v3.Score}||{v3.Turns}"
            : s.StatusLine;
        Emit(new OutputEvent(OutputKind.Status, status));
    }
}
