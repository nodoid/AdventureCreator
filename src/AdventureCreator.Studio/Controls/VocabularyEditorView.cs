using AdventureCreator.Core.Model;
using AdventureCreator.Core.Parsing;
using Theme = AdventureCreator.Maui.Theme;

namespace AdventureCreator.Studio.Controls;

/// <summary>
/// The game's vocabulary: for each kind of word, the game's own words (which can be typed in and removed) come
/// first, with the words that are built in (or learned from the items) listed underneath.
/// </summary>
public sealed class VocabularyEditorView : ContentView
{
    private readonly Adventure adventure;
    private readonly EditorContext ctx;
    private readonly VerticalStackLayout root = new() { Spacing = 6 };

    private static readonly string[] DirectionNames = BuiltInLexicon.Directions.Select(d => d.Canonical).ToArray();

    public VocabularyEditorView(Adventure adventure, EditorContext ctx)
    {
        this.adventure = adventure;
        this.ctx = ctx;
        Content = root;
        Build();
    }

    private Vocabulary Voc => adventure.Vocabulary;

    private void Build()
    {
        root.Children.Clear();
        root.Children.Add(new Label
        {
            Text = "Your words come first; words the parser already knows are listed underneath each group. Type new words in the box and press Enter or Add " +
                   "(several at once, separated by commas). New commands are added in the Commands section.",
            FontSize = 12, TextColor = Theme.SecondaryText,
        });

        var itemNouns = adventure.Items.SelectMany(i => i.Nouns).Select(n => n.ToLowerInvariant()).Distinct().Order().ToList();
        var itemAdjectives = adventure.Items.SelectMany(i => i.Adjectives).Select(n => n.ToLowerInvariant()).Distinct().Order().ToList();

        WordList("Nouns", "Words for things that aren't items (scenery, ideas) so the parser recognises them. Item nouns are set on each item.",
            Voc.Nouns, ("Learned from your items", itemNouns));
        WordList("Adjectives", "Describing words the parser should accept.",
            Voc.Adjectives, ("Learned from your items", itemAdjectives), ("Built in", BuiltInLexicon.Adjectives.ToList()));
        WordList("Adverbs", "How something is done (quietly, carefully…); triggers can test them.",
            Voc.Adverbs, ("Built in", BuiltInLexicon.Adverbs.ToList()));
        WordList("Prepositions", "Linking words such as in, on, under.",
            Voc.Prepositions, ("Built in", BuiltInLexicon.Prepositions.ToList()));
        WordList("Ignored words", "Words the parser skips.",
            Voc.IgnoredWords, ("Built in", BuiltInLexicon.Ignored.Concat(BuiltInLexicon.Articles).ToList()));
        DirectionList();
        ReplacementList();
    }

    // ================================================================= word lists

    private void WordList(string title, string help, List<string> words, params (string Title, List<string> Words)[] builtIn)
    {
        var body = new VerticalStackLayout { Spacing = 6 };
        body.Children.Add(new Label { Text = help, FontSize = 12, TextColor = Theme.SecondaryText });

        var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var w in words.ToList())
            chips.Children.Add(Tag(w, () => { words.Remove(w); Changed(); }));
        if (words.Count == 0) chips.Children.Add(new Label { Text = "No words of your own yet.", FontSize = 12, TextColor = Theme.SecondaryText, Margin = new Thickness(0, 4) });
        body.Children.Add(chips);

        body.Children.Add(AddRow($"Add {title.ToLowerInvariant()}…", text =>
        {
            foreach (var w in Split(text))
                if (!words.Contains(w, StringComparer.OrdinalIgnoreCase)) words.Add(w);
            Changed();
        }));

        foreach (var (subtitle, list) in builtIn)
        {
            if (list.Count == 0) continue;
            body.Children.Add(new Label { Text = subtitle.ToUpperInvariant(), FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Theme.SecondaryText, Margin = new Thickness(0, 6, 0, 0) });
            body.Children.Add(new Label { Text = string.Join(", ", list), FontSize = 12, TextColor = Theme.SecondaryText });
        }
        root.Children.Add(new SectionView($"{title} ({words.Count})", body));
    }

    private void DirectionList()
    {
        var body = new VerticalStackLayout { Spacing = 6 };
        body.Children.Add(new Label { Text = "Extra words for exit directions (fore, aft, port…), each meaning one of the standard directions.", FontSize = 12, TextColor = Theme.SecondaryText });
        var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (w0, dir) in Voc.Directions.ToList())
            chips.Children.Add(Tag($"{w0} → {dir}", () => { Voc.Directions.Remove(w0); Changed(); }));
        if (Voc.Directions.Count == 0) chips.Children.Add(new Label { Text = "No direction words of your own yet.", FontSize = 12, TextColor = Theme.SecondaryText, Margin = new Thickness(0, 4) });
        body.Children.Add(chips);

        var word = new Entry { Placeholder = "New word (e.g. fore)", FontSize = 13, WidthRequest = 220 };
        var picker = new Picker { ItemsSource = DirectionNames, SelectedIndex = 0, FontSize = 13, WidthRequest = 160 };
        var add = new Chip("Add", "Add this direction word");
        void Commit()
        {
            var w = Split(word.Text ?? "").FirstOrDefault();
            if (w == null || picker.SelectedItem is not string d) return;
            Voc.Directions[w] = d;
            Changed();
        }
        add.Clicked += (_, _) => Commit();
        word.Completed += (_, _) => Commit();
        body.Children.Add(new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Children = { word, new Label { Text = "means", FontSize = 12, Margin = new Thickness(8, 0), VerticalOptions = LayoutOptions.Center, TextColor = Theme.Text }, picker, add },
        });

        body.Children.Add(new Label { Text = "BUILT IN", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Theme.SecondaryText, Margin = new Thickness(0, 6, 0, 0) });
        body.Children.Add(new Label { Text = string.Join("   ", BuiltInLexicon.Directions.Select(d => $"{d.Canonical}: {string.Join(", ", d.Words)}")), FontSize = 12, TextColor = Theme.SecondaryText });
        root.Children.Add(new SectionView($"Directions ({Voc.Directions.Count})", body));
    }

    private void ReplacementList()
    {
        var body = new VerticalStackLayout { Spacing = 6 };
        body.Children.Add(new Label { Text = "Phrases replaced before the sentence is understood, e.g. \"xyzzy\" → \"say xyzzy\" or \"hit the road\" → \"go north\".", FontSize = 12, TextColor = Theme.SecondaryText });
        var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (f0, t0) in Voc.Replacements.ToList())
            chips.Children.Add(Tag($"{f0} → {t0}", () => { Voc.Replacements.Remove(f0); Changed(); }));
        if (Voc.Replacements.Count == 0) chips.Children.Add(new Label { Text = "No replacements of your own yet.", FontSize = 12, TextColor = Theme.SecondaryText, Margin = new Thickness(0, 4) });
        body.Children.Add(chips);

        var from = new Entry { Placeholder = "When the player types…", FontSize = 13, WidthRequest = 220 };
        var to = new Entry { Placeholder = "…understand it as", FontSize = 13, WidthRequest = 220 };
        var add = new Chip("Add", "Add this replacement");
        void Commit()
        {
            var f = (from.Text ?? "").Trim().ToLowerInvariant();
            var t = (to.Text ?? "").Trim().ToLowerInvariant();
            if (f.Length == 0 || t.Length == 0) return;
            Voc.Replacements[f] = t;
            Changed();
        }
        add.Clicked += (_, _) => Commit();
        to.Completed += (_, _) => Commit();
        body.Children.Add(new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Children = { from, new Label { Text = "→", FontSize = 14, Margin = new Thickness(8, 0), VerticalOptions = LayoutOptions.Center, TextColor = Theme.Text }, to, add },
        });
        body.Children.Add(new Label { Text = "BUILT IN", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Theme.SecondaryText, Margin = new Thickness(0, 6, 0, 0) });
        body.Children.Add(new Label { Text = string.Join("   ", BuiltInLexicon.Idioms.Select(kv => $"{kv.Key} → {kv.Value}")), FontSize = 12, TextColor = Theme.SecondaryText });
        root.Children.Add(new SectionView($"Replacements ({Voc.Replacements.Count})", body));
    }

    // ================================================================= helpers

    private View AddRow(string placeholder, Action<string> add)
    {
        var entry = new Entry { Placeholder = placeholder, FontSize = 13 };
        var button = new Chip("Add", "Add the words typed");
        void Commit()
        {
            if (string.IsNullOrWhiteSpace(entry.Text)) return;
            add(entry.Text);
        }
        entry.Completed += (_, _) => Commit();
        button.Clicked += (_, _) => Commit();
        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6, WidthRequest = 460, HorizontalOptions = LayoutOptions.Start };
        row.Add(entry, 0);
        row.Add(button, 1);
        return row;
    }

    private static IEnumerable<string> Split(string text) =>
        text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Trim().Contains(' ') && !part.Contains('"') ? part.Split(' ', StringSplitOptions.RemoveEmptyEntries) : new[] { part })
            .Select(w => w.Trim().Trim('"').ToLowerInvariant())
            .Where(w => w.Length > 0);

    /// <summary>A word with a ✕ to remove it.</summary>
    private static View Tag(string text, Action remove)
    {
        var x = new Label { Text = "✕", FontSize = 11, TextColor = Theme.SecondaryText, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(4, 0, 0, 0) };
        x.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(remove) });
        ToolTipProperties.SetText(x, "Remove");
        return new Border
        {
            Padding = new Thickness(9, 4, 7, 4), Margin = new Thickness(0, 0, 6, 6),
            BackgroundColor = Theme.Chip, Stroke = Theme.ChipBorder, StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
            Content = new HorizontalStackLayout { Children = { new Label { Text = text, FontSize = 12, TextColor = Theme.Text, VerticalOptions = LayoutOptions.Center }, x } },
        };
    }

    private void Changed()
    {
        ctx.Changed(Voc);
        Build();
    }
}
