using AdventureSystem.Core.ZMachine;
using System.Text;
using System.Text.RegularExpressions;
using AdventureSystem.Core.Model;
using AdventureSystem.Core.Parsing;

namespace AdventureSystem.Core.Engine;

/// <summary>
/// Runs an <see cref="Adventure"/>. Hosts call <see cref="Start"/> once and then <see cref="Submit"/> for every line the
/// player types; each call returns the output (text, pictures, sounds…) as a <see cref="TurnResult"/>.
/// </summary>
public sealed partial class GameEngine
{
    public Adventure Adventure { get; }
    public GameState State { get; private set; }
    public Lexicon Lexicon { get; }
    public Parser Parser { get; }
    public ISaveStorage SaveStorage { get; set; } = new MemorySaveStorage();
    public Random Random { get; set; }

    private TurnResult output = new();
    private readonly StringBuilder line = new();
    private readonly Stack<string> undo = new();
    private string? lastInput;
    private string currentInput = "";
    private string? lastFailedInput;
    private string? lastUnknownWord;
    private PendingQuestion? pending;
    private bool started;
    private int triggerDepth;

    public bool IsGameOver => State.GameOver;
    public Room? CurrentRoom => Adventure.FindRoom(State.CurrentRoomId);

    /// <summary>Raised for every output event as it is produced (in addition to being returned in the TurnResult).</summary>
    public event Action<OutputEvent>? Output;

    public GameEngine(Adventure adventure, int? randomSeed = null)
    {
        Adventure = adventure;
        Lexicon = new Lexicon(adventure);
        Parser = new Parser(Lexicon) { SpellingCorrection = adventure.Settings.SpellingCorrection };
        Random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        State = GameState.Initial(adventure);
        if (adventure.IsStory) story = ZStory.CreateMachine(adventure, randomSeed);
    }

    // ================================================================ public API

    public TurnResult Start()
    {
        if (story != null) return StartStory();
        BeginOutput();
        StartCore();
        EmitStatus();
        return EndOutput();
    }

    private void StartCore()
    {
        State = GameState.Initial(Adventure);
        undo.Clear();
        pending = null;
        started = true;

        if (Adventure.IntroPictureId != null && Adventure.Settings.ShowPictures)
            Emit(new OutputEvent(OutputKind.Picture, Id: Adventure.IntroPictureId));
        if (Adventure.IntroSoundId != null)
            PlaySound(Adventure.IntroSoundId, false);
        if (!string.IsNullOrWhiteSpace(Adventure.Introduction))
            Say(Format(Adventure.Introduction, null));

        var ctx = new CommandContext(this, null);
        RunEventTriggers(TriggerEvent.GameStart, ctx);
        if (!State.GameOver)
        {
            EnterRoom(State.CurrentRoomId, ctx, initial: true);
        }
    }

    public TurnResult Submit(string input)
    {
        if (story != null) return SubmitStory(input);
        if (!started) Start();
        BeginOutput();
        try
        {
            ProcessInput(input ?? "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Say("[Internal error: " + ex.Message + "]", TextStyle.Error);
        }
        EmitStatus();
        return EndOutput();
    }

    /// <summary>Current game state as a string (save game).</summary>
    public string SaveToString() => State.Serialize();

    public void RestoreFromString(string data)
    {
        State = GameState.Deserialize(data);
        started = true;
        pending = null;
    }

    // ================================================================ input processing

    private sealed class PendingQuestion
    {
        public required ParsedCommand Command;
        public required bool SecondSlot;
        public required List<Item> Candidates;
        public string Input = "";
    }

    private void ProcessInput(string input)
    {
        input = input.Trim();
        var lower = input.ToLowerInvariant();

        if (State.GameOver)
        {
            var w = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (w is "restart" or "reset") { DoRestart(); return; }
            if (w is "restore" or "load") { DoRestore(input.Contains(' ') ? input[(input.IndexOf(' ') + 1)..] : null); return; }
            if (w is "undo" && Adventure.Settings.AllowUndo) { DoUndo(); return; }
            if (w is "quit" or "q") { Emit(new OutputEvent(OutputKind.Quit)); return; }
            Say("The game is over. You can RESTART, RESTORE a saved game" + (Adventure.Settings.AllowUndo ? ", UNDO the last move" : "") + " or QUIT.", TextStyle.System);
            return;
        }

        if (input.Length == 0)
        {
            Say("I beg your pardon?");
            return;
        }

        if (lower is "again" or "g" or "repeat")
        {
            if (lastInput == null) { Say("You haven't done anything yet."); return; }
            input = lastInput;
            lower = input.ToLowerInvariant();
        }
        else if (lower.StartsWith("oops ") || lower.StartsWith("o "))
        {
            var fix = input[(input.IndexOf(' ') + 1)..].Trim();
            if (lastFailedInput == null || lastUnknownWord == null) { Say("Sorry, that can't be corrected."); return; }
            input = Regex.Replace(lastFailedInput, $@"\b{Regex.Escape(lastUnknownWord)}\b", fix, RegexOptions.IgnoreCase);
            lower = input.ToLowerInvariant();
        }

        if (pending != null)
        {
            var p = pending;
            pending = null;
            if (TryAnswerQuestion(p, input)) { lastInput = p.Input; return; }
        }

        currentInput = input;
        var outcome = Parser.Parse(input);
        foreach (var (from, to) in outcome.Corrections)
            Say(Msg(Engine.Msg.CorrectedSpelling, null, ("word", from), ("corrected", to)), TextStyle.System);

        bool undoSaved = false;
        foreach (var cmd in outcome.Commands)
        {
            if (!cmd.IsMeta && Adventure.Settings.AllowUndo && !undoSaved)
            {
                undo.Push(State.Serialize());
                if (undo.Count > 50) TrimUndo();
                undoSaved = true;
            }
            ExecuteCommand(cmd, null, null);
            if (pending != null) { pending.Input = input; break; }
            if (State.GameOver) break;
        }

        if (outcome.Error != null && pending == null && !State.GameOver)
        {
            var ctx = new CommandContext(this, null) { UnknownWord = outcome.UnknownWord };
            var r = RunEventTriggers(TriggerEvent.Unhandled, ctx);
            if (!r.AnyFired)
            {
                if (outcome.Error == "UNKNOWN")
                {
                    Say(Msg(Engine.Msg.UnknownWord, ctx, ("word", outcome.UnknownWord ?? "")));
                    lastFailedInput = input;
                    lastUnknownWord = outcome.UnknownWord;
                }
                else Say(outcome.Error);
            }
        }
        if (outcome.Error == null) lastInput = input;
    }

    private void TrimUndo()
    {
        var keep = undo.Take(40).Reverse().ToList();
        undo.Clear();
        foreach (var s in keep) undo.Push(s);
    }

    private bool TryAnswerQuestion(PendingQuestion p, string input)
    {
        var words = input.ToLowerInvariant().Split(new[] { ' ', ',', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Lexicon.Normalize)
            .Where(w => !Lexicon.Is(w, WordKind.Article) && w != "one")
            .ToList();
        if (words.Count == 0) return false;
        // A new command rather than an answer?
        if (Lexicon.MatchVerbPhrases(words).Any() && !p.Candidates.Any(c => ItemWords(c).Contains(words[0]))) return false;
        if (words.Count == 1 && Lexicon.Direction(words[0]) != null && !p.Candidates.Any(c => ItemWords(c).Contains(words[0]))) return false;

        List<Item> chosen;
        if (words.Any(w => Lexicon.Is(w, WordKind.Quantifier)))
            chosen = p.Candidates;
        else if (words.Count == 1 && BuiltInLexicon.Ordinals.TryGetValue(words[0], out var ord))
            chosen = new List<Item> { ord == -1 ? p.Candidates[^1] : p.Candidates[Math.Clamp(ord - 1, 0, p.Candidates.Count - 1)] };
        else
            chosen = p.Candidates.Where(c => words.All(w => ItemWords(c).Any(iw => Lexicon.Equivalent(iw, w)))).ToList();

        if (chosen.Count == 0) return false;
        if (chosen.Count > 1 && !(p.SecondSlot ? p.Command.Object2Slot : p.Command.Object1Slot).GetValueOrDefault().IsMulti())
        {
            AskWhich(p.Command, p.SecondSlot, chosen);
            pending!.Input = p.Input;
            return true;
        }
        if (Adventure.Settings.AllowUndo) undo.Push(State.Serialize());
        if (p.SecondSlot) ExecuteCommand(p.Command, null, chosen);
        else ExecuteCommand(p.Command, chosen, null);
        return true;
    }

    private HashSet<string> ItemWords(Item item)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in NounsOf(item)) { set.Add(n); foreach (var p in n.Split(' ')) set.Add(p); }
        foreach (var a in item.Adjectives) set.Add(a);
        foreach (var w in item.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)) set.Add(w);
        return set;
    }

    private void AskWhich(ParsedCommand cmd, bool second, List<Item> candidates)
    {
        pending = new PendingQuestion { Command = cmd, SecondSlot = second, Candidates = candidates };
        var names = candidates.Select(c => c.WithDefinite()).ToList();
        Say(Msg(Engine.Msg.Which, null, ("list", JoinList(names, "or"))));
    }

    // ================================================================ output helpers

    private void BeginOutput()
    {
        output = new TurnResult();
        line.Clear();
    }

    private TurnResult EndOutput()
    {
        FlushLine();
        output.GameOver = State.GameOver;
        output.Won = State.Won;
        var r = output;
        output = new TurnResult();
        return r;
    }

    private void Emit(OutputEvent e)
    {
        if (e.Kind != OutputKind.Text) FlushLine();
        output.Events.Add(e);
        Output?.Invoke(e);
    }

    private void FlushLine()
    {
        if (line.Length == 0) return;
        var e = new OutputEvent(OutputKind.Text, line.ToString());
        line.Clear();
        output.Events.Add(e);
        Output?.Invoke(e);
    }

    /// <summary>Prints a paragraph (or a fragment when <paramref name="newline"/> is false).</summary>
    /// <summary>Returned by <see cref="Msg"/> for messages the author blanked out; <see cref="Say"/> prints nothing.</summary>
    public const string Silent = "\u0000";

    public void Say(string text, TextStyle style = TextStyle.Normal, bool newline = true)
    {
        if (text == Silent) return;
        if (style != TextStyle.Normal)
        {
            FlushLine();
            Emit(new OutputEvent(OutputKind.Text, text + (newline ? "\n" : ""), Style: style));
            return;
        }
        line.Append(text);
        if (newline) { line.Append('\n'); FlushLine(); }
    }

    private void EmitStatus()
    {
        if (story != null) { EmitStoryStatus(); return; }
        var room = CurrentRoom;
        var health = Adventure.Settings.PlayerHealth > 0 ? $"|{State.Health}|{Adventure.Settings.PlayerHealth}" : "";
        Emit(new OutputEvent(OutputKind.Status, $"{room?.Name}|{State.Score}|{Adventure.ComputeMaxScore()}|{State.Turns}{health}", room?.Id));
    }

    public string Msg(string id, CommandContext? ctx, params (string Key, string Value)[] extra)
    {
        if (Adventure.Messages.TryGetValue(id, out var overridden) && overridden.Length == 0) return Silent;
        var text = Adventure.Messages.TryGetValue(id, out var custom) ? custom
            : Engine.Msg.Defaults.TryGetValue(id, out var def) ? def : id;
        return Format(text, ctx, extra);
    }

    private static readonly Regex placeholder = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    /// <summary>Expands {placeholders} in author text.</summary>
    public string Format(string text, CommandContext? ctx, params (string Key, string Value)[] extra)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
        bool emptied = false;
        var formatted = placeholder.Replace(text, m =>
        {
            var key = m.Groups[1].Value.Trim();
            foreach (var (k, v) in extra)
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
            var lowerKey = key.ToLowerInvariant();
            bool capital = key.Length > 0 && char.IsUpper(key[0]);

            string? Cap(string? s) => s is null ? null : capital && s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

            if (lowerKey.StartsWith("var:"))
                return GetVar(key[4..].Trim()).ToString();
            if (lowerKey.StartsWith("item:"))
                return Adventure.FindItem(key[5..].Trim())?.Name ?? "";
            if (lowerKey.StartsWith("room:"))
                return Adventure.FindRoom(key[5..].Trim())?.Name ?? "";

            string? result = lowerKey switch
            {
                "noun1" or "noun" => ctx?.Item1?.Name ?? ctx?.Word1,
                "noun2" => ctx?.Item2?.Name ?? ctx?.Word2,
                "the noun1" or "the noun" => ctx?.Item1?.WithDefinite() ?? (ctx?.Word1 is { } w1 ? "the " + w1 : "it"),
                "the noun2" => ctx?.Item2?.WithDefinite() ?? (ctx?.Word2 is { } w2 ? "the " + w2 : "it"),
                "a noun1" or "a noun" => ctx?.Item1?.WithArticle() ?? ctx?.Word1,
                "a noun2" => ctx?.Item2?.WithArticle() ?? ctx?.Word2,
                "verb" => ctx?.Command?.VerbWords,
                "adverb" => ctx?.Command?.Adverbs.FirstOrDefault() ?? "",
                "direction" => ctx?.Command?.Direction,
                "text" or "topic" => ctx?.Command?.Text,
                "number" => ctx?.Command?.Number?.ToString(),
                "score" => State.Score.ToString(),
                "max" or "maxscore" => Adventure.ComputeMaxScore().ToString(),
                "turns" => State.Turns.ToString(),
                "room" => CurrentRoom?.Name,
                "title" => Adventure.Title,
                "author" => Adventure.Author,
                "npc" => ctx?.Npc?.Name ?? ctx?.Actor?.Name,
                "the npc" => (ctx?.Npc ?? ctx?.Actor)?.WithDefinite(),
                "a npc" => (ctx?.Npc ?? ctx?.Actor)?.WithArticle(),
                "health" => State.Health.ToString(),
                "maxhealth" => Adventure.Settings.PlayerHealth.ToString(),
                "eventroom" => Adventure.FindRoom(ctx?.EventRoomId)?.Name ?? "",
                "actor" => ctx?.Actor?.Name,
                "the actor" => ctx?.Actor?.WithDefinite(),
                "word" => ctx?.UnknownWord,
                "carried" => JoinList(Carried().Select(i => i.WithArticle()).ToList()),
                "newline" or "br" => "\n",
                _ => null,
            };
            if (result is null) return m.Value;
            if (result.Length == 0) emptied = true;
            return Cap(result)!;
        });
        // An empty placeholder ("{adverb}" with no adverb) must not leave a double space or a space before punctuation.
        if (emptied) formatted = Regex.Replace(formatted, @"(?<=\S) {2,}(?=\S)| (?=[.,;:!?])", m => m.Value.Length > 1 ? " " : "");
        return formatted;
    }

    public static string JoinList(IReadOnlyList<string> items, string conjunction = "and")
    {
        return items.Count switch
        {
            0 => "",
            1 => items[0],
            2 => $"{items[0]} {conjunction} {items[1]}",
            _ => string.Join(", ", items.Take(items.Count - 1)) + $" {conjunction} " + items[^1],
        };
    }

    public void PlaySound(string? soundId, bool loop)
    {
        if (string.IsNullOrEmpty(soundId)) return;
        if (loop) State.CurrentSoundId = soundId;
        Emit(new OutputEvent(OutputKind.PlaySound, Id: soundId, Loop: loop));
    }

    public void StopSound()
    {
        State.CurrentSoundId = null;
        Emit(new OutputEvent(OutputKind.StopSound));
    }

    /// <summary>
    /// Shows a picture; with <paramref name="over"/>, draws it over the picture already shown instead (an 8-bit game's
    /// screen frame, say). The event's Id is the picture and its Text the pictures drawn over it, comma-separated.
    /// </summary>
    public void ShowPicture(string? pictureId, bool over = false)
    {
        if (!Adventure.Settings.ShowPictures) return;
        if (over && pictureId != null && State.CurrentPictureId != null)
        {
            if (string.Equals(pictureId, State.CurrentPictureId, StringComparison.OrdinalIgnoreCase)) return;
            State.PictureLayers.RemoveAll(l => string.Equals(l, pictureId, StringComparison.OrdinalIgnoreCase));
            State.PictureLayers.Add(pictureId);
        }
        else
        {
            State.CurrentPictureId = pictureId;
            State.PictureLayers.Clear();
        }
        Emit(new OutputEvent(OutputKind.Picture, Id: State.CurrentPictureId, Text: State.PictureLayers.Count > 0 ? string.Join(",", State.PictureLayers) : null));
    }

    // ================================================================ variables

    public int GetVar(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "@room": return Adventure.Rooms.FindIndex(r => string.Equals(r.Id, State.CurrentRoomId, StringComparison.OrdinalIgnoreCase));
            case "@turns": return State.Turns;
            case "@score": return State.Score;
            case "@carried": return Carried().Count(i => Loc(i) == Locations.Carried);
            case "@maxscore": return Adventure.ComputeMaxScore();
            case "@health": return State.Health;
        }
        return State.Variables.TryGetValue(name, out var v) ? v : 0;
    }

    public void SetVar(string name, int value)
    {
        switch (name.ToLowerInvariant())
        {
            case "@score": State.Score = value; return;
            case "@turns": State.Turns = value; return;
            case "@health": State.Health = Math.Max(0, value); return;
            case "@room":
                if (value >= 0 && value < Adventure.Rooms.Count) State.CurrentRoomId = Adventure.Rooms[value].Id;
                return;
            case "@carried": case "@maxscore": return;
        }
        State.Variables[name] = value;
    }
}

internal static class SlotKindExtensions
{
    public static bool IsMulti(this SlotKind k) => k is SlotKind.Multi or SlotKind.MultiHeld;
}
