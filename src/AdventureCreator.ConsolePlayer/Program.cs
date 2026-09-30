using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;
using AdventureCreator.Core.Samples;

// AdventureCreator console player.
//   - Run with no arguments inside an exported game: plays the game appended to the executable.
//   - adventure-player <game.adventure|game.json>     play a file
//   - adventure-player --example | --lighthouse        play a built-in example (Genesis / The Lighthouse)
//   - adventure-player --write-example <path> [lighthouse]  save an example game package
//   - adventure-player --parse <game> "command"        show how the parser understands a command
//   - adventure-player --script <game> <file>          run commands from a file (one per line)

var argList = args.ToList();
Adventure? game = null;

try
{
    if (argList.Count >= 2 && argList[0] == "--write-example")
    {
        AdventurePackage.Save(argList.Count > 2 && argList[2] == "lighthouse" ? ExampleAdventures.Lighthouse() : ExampleAdventures.Genesis(), argList[1]);
        Console.WriteLine($"Wrote {argList[1]}");
        return 0;
    }
    if (argList.Count >= 3 && argList[0] == "--parse")
    {
        var adv = argList[1] == "example" ? ExampleAdventures.Genesis() : argList[1] == "lighthouse" ? ExampleAdventures.Lighthouse() : AdventurePackage.Load(argList[1]);
        var engine = new GameEngine(adv);
        var outcome = engine.Parser.Parse(argList[2]);
        foreach (var c in outcome.Commands) Console.WriteLine(c + (c.Lenient ? "  (lenient: " + c.GrammarError + ")" : "") + "  grammar: " + c.Grammar);
        if (outcome.Error != null) Console.WriteLine("error: " + outcome.Error + " " + outcome.UnknownWord);
        foreach (var (f, t) in outcome.Corrections) Console.WriteLine($"corrected {f} -> {t}");
        return 0;
    }

    string? script = null;
    if (argList.Count >= 3 && argList[0] == "--script")
    {
        script = argList[2];
        argList = new List<string> { argList[1] };
    }

    if (argList.Count > 0 && argList[0] is "--example" or "example") game = ExampleAdventures.Genesis();
    else if (argList.Count > 0 && argList[0] is "--lighthouse" or "lighthouse") game = ExampleAdventures.Lighthouse();
    else if (argList.Count > 0) game = AdventurePackage.LoadAny(File.ReadAllBytes(argList[0]), argList[0]);
    else game = StandaloneExporter.LocateEmbeddedGame();

    if (game == null)
    {
        Console.WriteLine("Adventure Creator Player 1.0 – Copyright © 2026 Paul F.Johnson");
        Console.WriteLine("usage: adventure-player <game.adventure | story.z5 | story.zblorb> | --example | --write-example <path> | --parse <game|example> \"command\"");
        return 1;
    }

    return Play(game, script);
}
catch (Exception ex)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 2;
}

static int Play(Adventure game, string? scriptFile)
{
    var saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdventureCreator", "Saves",
        StandaloneExporter.SafeFileName(game.Title));
    var engine = new GameEngine(game) { SaveStorage = new FileSaveStorage(saveDir) };
    Console.Title = game.Title;
    Console.WriteLine(game.Title.ToUpperInvariant());
    if (!string.IsNullOrWhiteSpace(game.Author)) Console.WriteLine("by " + game.Author);
    Console.WriteLine();

    Render(engine.Start());
    var lines = scriptFile != null ? new Queue<string>(File.ReadAllLines(scriptFile)) : null;
    while (true)
    {
        Console.Write(game.Settings.Prompt);
        string? input;
        if (lines != null)
        {
            if (lines.Count == 0) break;
            input = lines.Dequeue();
            Console.WriteLine(input);
        }
        else input = Console.ReadLine();
        if (input == null) break;
        var result = engine.Submit(input);
        if (Render(result)) break;
    }
    return 0;
}

/// <summary>Writes a turn's output. Returns true when the player quit.</summary>
static bool Render(TurnResult result)
{
    bool quit = false;
    foreach (var e in result.Events)
    {
        switch (e.Kind)
        {
            case OutputKind.Text:
                var color = Console.ForegroundColor;
                if (e.Style == TextStyle.RoomTitle) { Console.WriteLine(); Console.ForegroundColor = ConsoleColor.Yellow; }
                else if (e.Style == TextStyle.System) Console.ForegroundColor = ConsoleColor.DarkCyan;
                else if (e.Style == TextStyle.Emphasis) Console.ForegroundColor = ConsoleColor.Magenta;
                else if (e.Style == TextStyle.Error) Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(Wrap(e.Text ?? ""));
                Console.ForegroundColor = color;
                break;
            case OutputKind.ClearScreen:
                try { Console.Clear(); } catch (IOException) { }
                break;
            case OutputKind.Beep:
                Console.Beep();
                break;
            case OutputKind.Pause:
                Thread.Sleep(Math.Clamp(e.Milliseconds, 0, 5000));
                break;
            case OutputKind.Quit:
                quit = true;
                break;
        }
    }
    return quit;
}

static string Wrap(string text)
{
    int width;
    try { width = Math.Max(40, Console.WindowWidth - 1); } catch { width = 79; }
    var sb = new System.Text.StringBuilder();
    foreach (var paragraph in text.Split('\n'))
    {
        int col = 0;
        foreach (var word in paragraph.Split(' '))
        {
            if (col > 0 && col + word.Length + 1 > width) { sb.Append('\n'); col = 0; }
            else if (col > 0) { sb.Append(' '); col++; }
            sb.Append(word);
            col += word.Length;
        }
        sb.Append('\n');
    }
    return sb.ToString(0, Math.Max(0, sb.Length - 1));
}
