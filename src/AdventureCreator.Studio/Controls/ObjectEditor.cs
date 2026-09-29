using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using AdventureCreator.Core.Model;
using AdventureCreator.Studio.Services;

namespace AdventureCreator.Studio.Controls;

/// <summary>Shared services for editors: the document and a page to show dialogs on.</summary>
public sealed class EditorContext
{
    public EditorContext(StudioDocument document, Func<Page> page)
    {
        Document = document;
        PageProvider = page;
    }

    public StudioDocument Document { get; }
    public Func<Page> PageProvider { get; }
    public Adventure Adventure => Document.Adventure;
    public void Changed(object? what) => Document.MarkChanged(what);
}

/// <summary>
/// Builds a form for any model object by reflection: text boxes, numeric fields, check boxes, enum pickers,
/// id pickers for references, and nested list editors for exits, conditions, actions, topics…
/// </summary>
public sealed class ObjectEditor : ContentView
{
    private static readonly HashSet<string> MultiLine = new()
    {
        "Description", "Introduction", "Text", "ReadText", "RoomDescription", "Response", "SolvedMessage", "DefaultResponse",
        "ShortDescription", "Notes", "Help", "BlockedMessage", "TravelMessage",
    };

    private static readonly HashSet<string> LineLists = new() { "Hints", "Grammar", "Notes" };

    private static readonly HashSet<string> Hidden = new()
    {
        "Assets", "FormatVersion", "Palette", "Commands", "Points", "Pattern", "LegacyWordNumbers", "PrimaryNoun",
    };

    private readonly object target;
    private readonly EditorContext ctx;
    private readonly HashSet<string>? only;
    private readonly HashSet<string> exclude;
    private readonly Action? onStructureChanged;

    public ObjectEditor(object target, EditorContext ctx, IEnumerable<string>? only = null, IEnumerable<string>? exclude = null, Action? onStructureChanged = null)
    {
        this.target = target;
        this.ctx = ctx;
        this.only = only?.ToHashSet();
        this.exclude = exclude?.ToHashSet() ?? new HashSet<string>();
        this.onStructureChanged = onStructureChanged;
        Build();
    }

    public void Build()
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(170)), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12,
            RowSpacing = 8,
        };
        int row = 0;
        foreach (var prop in Properties(target.GetType()))
        {
            if (only != null && !only.Contains(prop.Name)) continue;
            if (exclude.Contains(prop.Name)) continue;
            var control = CreateControl(prop);
            if (control == null) continue;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            bool fullWidth = control is ListEditorBase || control is SectionView;
            if (fullWidth)
            {
                grid.Add(control, 0, row);
                Grid.SetColumnSpan(control, 2);
            }
            else
            {
                var label = new Label
                {
                    Text = Humanize(prop.Name),
                    VerticalOptions = LayoutOptions.Start,
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalTextAlignment = TextAlignment.End,
                    LineBreakMode = LineBreakMode.WordWrap,
                };
                grid.Add(label, 0, row);
                grid.Add(control, 1, row);
            }
            row++;
        }
        Content = grid;
    }

    public static IEnumerable<PropertyInfo> Properties(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .Where(p => !Hidden.Contains(p.Name));

    public static string Humanize(string name)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", " $1");
        s = s.Replace(" Id", " id").Replace("Item Id", "item").Replace("Room Id", "room");
        return s;
    }

    private void Changed() => ctx.Changed(target);

    private View? CreateControl(PropertyInfo prop)
    {
        var type = prop.PropertyType;
        var value = prop.GetValue(target);

        if (type == typeof(string))
        {
            if (!prop.CanWrite) return null;
            var kind = References.KindOf(target, prop.Name);
            if (kind != RefKind.None)
                return new ReferenceField(ctx, (string?)value, kind, v => { prop.SetValue(target, string.IsNullOrEmpty(v) && Nullable(prop) ? null : v ?? ""); Changed(); },
                    allowFreeText: kind is RefKind.Verb or RefKind.Adverb or RefKind.Preposition or RefKind.Item or RefKind.Direction or RefKind.Location or RefKind.Variable or RefKind.Room);
            if (MultiLine.Contains(prop.Name))
            {
                var ed = new Editor { Text = (string?)value, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 70 };
                ed.TextChanged += (_, e) => { prop.SetValue(target, e.NewTextValue); Changed(); };
                return ed;
            }
            var entry = new Entry { Text = (string?)value };
            entry.TextChanged += (_, e) => { prop.SetValue(target, e.NewTextValue); Changed(); };
            return entry;
        }

        if (type == typeof(int) || type == typeof(double))
        {
            if (!prop.CanWrite) return null;
            var entry = new Entry { Text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), Keyboard = Keyboard.Numeric, WidthRequest = 120, HorizontalOptions = LayoutOptions.Start };
            entry.TextChanged += (_, e) =>
            {
                if (type == typeof(int) && int.TryParse(e.NewTextValue, out var i)) { prop.SetValue(target, i); Changed(); }
                else if (type == typeof(double) && double.TryParse(e.NewTextValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) { prop.SetValue(target, d); Changed(); }
            };
            return entry;
        }

        if (type == typeof(bool))
        {
            if (!prop.CanWrite) return null;
            var cb = new CheckBox { IsChecked = (bool)value!, VerticalOptions = LayoutOptions.Center };
            cb.CheckedChanged += (_, e) => { prop.SetValue(target, e.Value); Changed(); };
            return new HorizontalStackLayout { Children = { cb } };
        }

        if (type.IsEnum)
        {
            if (!prop.CanWrite) return null;
            var names = Enum.GetNames(type);
            var picker = new Picker { ItemsSource = names, SelectedItem = value?.ToString(), HorizontalOptions = LayoutOptions.Start, MinimumWidthRequest = 220 };
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (picker.SelectedItem is not string s) return;
                prop.SetValue(target, Enum.Parse(type, s));
                Changed();
                // The meaning of other fields depends on the type (conditions/actions/triggers): rebuild.
                if (target is Condition or GameAction or Trigger) MainThread.BeginInvokeOnMainThread(Build);
                onStructureChanged?.Invoke();
            };
            return picker;
        }

        if (type == typeof(List<string>))
        {
            var list = (List<string>)value!;
            if (LineLists.Contains(prop.Name))
            {
                var ed = new Editor { Text = string.Join('\n', list), AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 60, Placeholder = "One per line" };
                ed.TextChanged += (_, e) =>
                {
                    list.Clear();
                    list.AddRange((e.NewTextValue ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0));
                    Changed();
                };
                return ed;
            }
            var entry = new Entry { Text = string.Join(", ", list), Placeholder = "Comma separated" };
            entry.TextChanged += (_, e) =>
            {
                list.Clear();
                list.AddRange((e.NewTextValue ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                Changed();
            };
            return entry;
        }

        if (type == typeof(Dictionary<string, string>))
        {
            var dict = (Dictionary<string, string>)value!;
            var ed = new Editor
            {
                Text = string.Join('\n', dict.Select(kv => $"{kv.Key} = {kv.Value}")),
                AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 60, Placeholder = "key = value (one per line)",
            };
            ed.TextChanged += (_, e) =>
            {
                dict.Clear();
                foreach (var line in (e.NewTextValue ?? "").Split('\n'))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    dict[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
                Changed();
            };
            return ed;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = type.GetGenericArguments()[0];
            if (elementType.IsPrimitive || elementType == typeof(uint)) return null;
            if (elementType == typeof(VerbDefinition) || elementType == typeof(DrawCommand)) return null;
            return new ListEditor((IList)value!, elementType, Humanize(prop.Name), ctx, target);
        }

        if (type.IsClass && type.Namespace == typeof(Adventure).Namespace && value != null)
            return new SectionView(Humanize(prop.Name), new ObjectEditor(value, ctx));

        return null;
    }

    private static bool Nullable(PropertyInfo p) =>
        new NullabilityInfoContext().Create(p).WriteState == NullabilityState.Nullable;
}

/// <summary>A titled group of fields.</summary>
public sealed class SectionView : ContentView
{
    public SectionView(string title, View content)
    {
        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new Label { Text = title, FontAttributes = FontAttributes.Bold, FontSize = 15 },
                new BoxView { HeightRequest = 1, Color = Colors.Gray.WithAlpha(0.3f) },
                content,
            },
        };
    }
}

public abstract class ListEditorBase : ContentView { }

/// <summary>Editable list of child objects with add / remove / reorder.</summary>
public sealed class ListEditor : ListEditorBase
{
    private readonly IList list;
    private readonly Type elementType;
    private readonly string title;
    private readonly EditorContext ctx;
    private readonly object owner;
    private readonly VerticalStackLayout rows = new() { Spacing = 6 };

    public ListEditor(IList list, Type elementType, string title, EditorContext ctx, object owner)
    {
        this.list = list;
        this.elementType = elementType;
        this.title = title;
        this.ctx = ctx;
        this.owner = owner;

        var add = new Button { Text = "+ Add", Padding = new Thickness(10, 2), HeightRequest = 30, FontSize = 13 };
        add.Clicked += (_, _) =>
        {
            var item = CreateElement();
            list.Add(item);
            ctx.Changed(owner);
            Rebuild();
        };
        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        header.Add(new Label { Text = $"{title}", FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }, 0);
        header.Add(add, 1);

        Content = new VerticalStackLayout { Spacing = 6, Margin = new Thickness(0, 10, 0, 0), Children = { header, rows } };
        Rebuild();
    }

    private object CreateElement()
    {
        var item = Activator.CreateInstance(elementType)!;
        if (item is Exit exit)
        {
            var used = list.Cast<Exit>().Select(e => e.Direction).ToHashSet();
            exit.Direction = new[] { "north", "south", "east", "west", "up", "down", "northeast", "northwest", "southeast", "southwest", "in", "out" }.FirstOrDefault(d => !used.Contains(d)) ?? "north";
        }
        if (item is GameAction ga) ga.Type = ActionType.Message;
        return item;
    }

    private void Rebuild()
    {
        rows.Children.Clear();
        if (list.Count == 0)
        {
            rows.Children.Add(new Label { Text = "(none)", Opacity = 0.5, FontSize = 13 });
            return;
        }
        for (int i = 0; i < list.Count; i++)
        {
            int index = i;
            var element = list[i]!;
            View editor = element switch
            {
                Condition c => new ConditionRow(c, ctx),
                GameAction a => new ActionRow(a, ctx),
                _ => new ObjectEditor(element, ctx),
            };

            var up = SmallButton("↑", "Move up", () => Move(index, -1));
            var down = SmallButton("↓", "Move down", () => Move(index, 1));
            var remove = SmallButton("✕", "Remove", () =>
            {
                list.RemoveAt(index);
                ctx.Changed(owner);
                Rebuild();
            });
            var tools = new VerticalStackLayout { Spacing = 2, Children = { up, down, remove } };
            var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6 };
            g.Add(editor, 0);
            g.Add(tools, 1);
            rows.Children.Add(new Border
            {
                Padding = new Thickness(10, 8),
                StrokeThickness = 1,
                Stroke = Colors.Gray.WithAlpha(0.35f),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                Content = g,
            });
        }
    }

    private void Move(int index, int delta)
    {
        int to = index + delta;
        if (to < 0 || to >= list.Count) return;
        var item = list[index];
        list.RemoveAt(index);
        list.Insert(to, item);
        ctx.Changed(owner);
        Rebuild();
    }

    private static Button SmallButton(string text, string tip, Action action)
    {
        var b = new Button { Text = text, WidthRequest = 30, HeightRequest = 26, Padding = 0, FontSize = 12 };
        ToolTipProperties.SetText(b, tip);
        b.Clicked += (_, _) => action();
        return b;
    }
}

/// <summary>Compact one-line editor for a condition: [NOT] type A B N.</summary>
public sealed class ConditionRow : ContentView
{
    public ConditionRow(Condition c, EditorContext ctx)
    {
        var hint = new Label { FontSize = 11, Opacity = 0.6 };
        var aField = new ReferenceField(ctx, c.A, () => References.KindOf(c, "A"), v => { c.A = string.IsNullOrEmpty(v) ? null : v; ctx.Changed(c); }, true) { Placeholder = "A", MinimumWidthRequest = 160 };
        var bField = new ReferenceField(ctx, c.B, () => References.KindOf(c, "B"), v => { c.B = string.IsNullOrEmpty(v) ? null : v; ctx.Changed(c); }, true) { Placeholder = "B", MinimumWidthRequest = 120 };
        var n = new Entry { Text = c.N.ToString(), Keyboard = Keyboard.Numeric, WidthRequest = 70, Placeholder = "N" };
        n.TextChanged += (_, e) => { if (int.TryParse(e.NewTextValue, out var v)) { c.N = v; ctx.Changed(c); } };
        var not = new CheckBox { IsChecked = c.Negate };
        not.CheckedChanged += (_, e) => { c.Negate = e.Value; ctx.Changed(c); };
        var type = new Picker { ItemsSource = Enum.GetNames<ConditionType>(), SelectedItem = c.Type.ToString(), MinimumWidthRequest = 190 };
        void UpdateHint() => hint.Text = References.Hint(c);
        type.SelectedIndexChanged += (_, _) =>
        {
            if (type.SelectedItem is string s) { c.Type = Enum.Parse<ConditionType>(s); ctx.Changed(c); UpdateHint(); }
        };
        UpdateHint();
        Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new FlexLayout
                {
                    Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
                    Children = { new Label { Text = "not", VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 2, 0) }, not, type, aField, bField, n },
                },
                hint,
            },
        };
    }
}

/// <summary>Compact editor for an action: type A B N and text.</summary>
public sealed class ActionRow : ContentView
{
    public ActionRow(GameAction a, EditorContext ctx)
    {
        var hint = new Label { FontSize = 11, Opacity = 0.6 };
        var aField = new ReferenceField(ctx, a.A, () => References.KindOf(a, "A"), v => { a.A = string.IsNullOrEmpty(v) ? null : v; ctx.Changed(a); }, true) { Placeholder = "A", MinimumWidthRequest = 160 };
        var bField = new ReferenceField(ctx, a.B, () => References.KindOf(a, "B"), v => { a.B = string.IsNullOrEmpty(v) ? null : v; ctx.Changed(a); }, true) { Placeholder = "B", MinimumWidthRequest = 120 };
        var n = new Entry { Text = a.N.ToString(), Keyboard = Keyboard.Numeric, WidthRequest = 70, Placeholder = "N" };
        n.TextChanged += (_, e) => { if (int.TryParse(e.NewTextValue, out var v)) { a.N = v; ctx.Changed(a); } };
        var text = new Editor { Text = a.Text, Placeholder = "Text", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 36 };
        text.TextChanged += (_, e) => { a.Text = string.IsNullOrEmpty(e.NewTextValue) ? null : e.NewTextValue; ctx.Changed(a); };
        var type = new Picker { ItemsSource = Enum.GetNames<ActionType>(), SelectedItem = a.Type.ToString(), MinimumWidthRequest = 190 };
        void Update()
        {
            hint.Text = References.Hint(a);
            text.IsVisible = a.Type is ActionType.Message or ActionType.Win or ActionType.Lose or ActionType.SetExit or ActionType.SetRoomDescription
                or ActionType.SetItemDescription or ActionType.GoTo || !string.IsNullOrEmpty(a.Text);
        }
        type.SelectedIndexChanged += (_, _) =>
        {
            if (type.SelectedItem is string s) { a.Type = Enum.Parse<ActionType>(s); ctx.Changed(a); Update(); }
        };
        Update();
        Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new FlexLayout
                {
                    Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
                    Children = { type, aField, bField, n },
                },
                text,
                hint,
            },
        };
    }
}
