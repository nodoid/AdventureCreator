using AdventureSystem.Studio.Services;

namespace AdventureSystem.Studio.Controls;

/// <summary>
/// A text field for an id (room, item, variable, verb…) with a ▾ button listing valid choices.
/// Free text is allowed where patterns such as "take|get" or "$noun1" make sense.
/// </summary>
public sealed class ReferenceField : ContentView
{
    private readonly Entry entry;
    private readonly EditorContext ctx;
    private readonly Func<RefKind> kind;
    private readonly Action<string?> setter;
    private bool suppress;

    public ReferenceField(EditorContext ctx, string? value, RefKind kind, Action<string?> setter, bool allowFreeText)
        : this(ctx, value, () => kind, setter, allowFreeText) { }

    public ReferenceField(EditorContext ctx, string? value, Func<RefKind> kind, Action<string?> setter, bool allowFreeText)
    {
        this.ctx = ctx;
        this.kind = kind;
        this.setter = setter;
        entry = new Entry { Text = value, IsReadOnly = !allowFreeText };
        entry.TextChanged += (_, e) =>
        {
            if (!suppress) setter(e.NewTextValue);
        };
        var pick = new Button { Text = "…", WidthRequest = 40, Padding = 0 };
        ToolTipProperties.SetText(pick, "Choose from a list");
        pick.Clicked += async (_, _) => await ChooseAsync();
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 4 };
        grid.Add(entry, 0);
        grid.Add(pick, 1);
        Content = grid;
    }

    public string? Placeholder
    {
        get => entry.Placeholder;
        set => entry.Placeholder = value;
    }

    public string? Text => entry.Text;

    private async Task ChooseAsync()
    {
        var k = kind();
        var choices = References.Choices(ctx.Adventure, k);
        if (choices.Count == 0)
        {
            await ctx.PageProvider().DisplayAlertAsync("Nothing to choose", k == RefKind.None ? "This field takes free text or a number." : $"There are no {k.ToString().ToLowerInvariant()}s yet.", "OK");
            return;
        }
        var labels = choices.Select(c => c.Label).ToArray();
        var picked = await ctx.PageProvider().DisplayActionSheetAsync($"Choose {k.ToString().ToLowerInvariant()}", "Cancel", null, labels);
        if (picked == null || picked == "Cancel") return;
        var value = choices.First(c => c.Label == picked).Value;
        // Pattern fields accept alternatives: holding the existing value and appending with | would be surprising, so replace.
        suppress = true;
        entry.Text = value;
        suppress = false;
        setter(value);
    }
}
