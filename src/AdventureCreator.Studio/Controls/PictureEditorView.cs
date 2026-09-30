using System.Collections.ObjectModel;
using System.Diagnostics;
using AdventureCreator.Core.Graphics;
using AdventureCreator.Core.Model;
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;
using Theme = AdventureCreator.Maui.Theme;

namespace AdventureCreator.Studio.Controls;

/// <summary>
/// WYSIWYG picture designer. Shapes are drawn, picked, moved, resized and recoloured directly on the canvas, which
/// shows the picture exactly as the game renders it. An inspector edits the selected shape and the picture settings,
/// and the underlying drawing commands (the text form of the picture) are in a panel that expands and collapses.
/// </summary>
public sealed class PictureEditorView : ContentView
{
    public enum Tool { Select, Line, Rectangle, FilledRectangle, Ellipse, FilledEllipse, Polygon, FilledPolygon, Freehand, Plot, Fill, Shade, Text, Block, Call, Stamp }

    private static readonly (Tool Tool, string Label, string Tip)[][] ToolGroups =
    {
        new[] { (Tool.Select, "↖ Select", "Click a shape to select it; drag it to move, drag its handles to reshape") },
        new[]
        {
            (Tool.Line, "╱ Line", "Drag to draw a line"),
            (Tool.Rectangle, "▭ Rect", "Drag to draw a rectangle"),
            (Tool.FilledRectangle, "▬ Box", "Drag to draw a filled rectangle"),
            (Tool.Ellipse, "◯ Oval", "Drag to draw an ellipse"),
            (Tool.FilledEllipse, "⬤ Disc", "Drag to draw a filled ellipse"),
            (Tool.Polygon, "⬠ Poly", "Click points; click the first point or Finish to close"),
            (Tool.FilledPolygon, "⬟ Shape", "Click points of a filled shape; click the first point or Finish to close"),
            (Tool.Freehand, "✎ Pen", "Drag to draw freehand"),
            (Tool.Plot, "· Plot", "Click to plot a pixel"),
        },
        new[]
        {
            (Tool.Fill, "▨ Fill", "Click to flood fill an area with the ink colour"),
            (Tool.Shade, "▦ Shade", "Click to flood fill an area with a pattern"),
        },
        new[]
        {
            (Tool.Text, "A Text", "Click to place text"),
            (Tool.Block, "▣ Attr", "Drag to set the ink of 8×8 cells (Spectrum mode)"),
            (Tool.Call, "⧉ Sub", "Click to place another picture"),
            (Tool.Stamp, "🖼 Image", "Drag to place an imported image"),
        },
    };

    private static readonly byte[][] ShadePatterns =
    {
        new byte[] { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 },
        new byte[] { 0x88, 0x00, 0x22, 0x00, 0x88, 0x00, 0x22, 0x00 },
        new byte[] { 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00 },
        new byte[] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA },
        new byte[] { 0x81, 0x42, 0x24, 0x18, 0x18, 0x24, 0x42, 0x81 },
        new byte[] { 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80 },
        new byte[] { 0xEE, 0xBB, 0xEE, 0xBB, 0xEE, 0xBB, 0xEE, 0xBB },
    };

    private const string CommandsExpandedKey = "PictureEditor.CommandsExpanded";

    private readonly Picture picture;
    private readonly EditorContext ctx;
    private readonly GraphicsView canvas;
    private readonly CanvasDrawable drawable;
    private readonly Dictionary<Tool, Chip> toolChips = new();
    private readonly Label subtitle = new() { FontSize = 12, TextColor = Theme.SecondaryText };
    private readonly Label status = new() { FontSize = 12, TextColor = Theme.SecondaryText, VerticalOptions = LayoutOptions.Center };
    private readonly HorizontalStackLayout optionsBar = new() { Spacing = 10, VerticalOptions = LayoutOptions.Center };
    private readonly VerticalStackLayout colourSection = new() { Spacing = 8 };
    private readonly VerticalStackLayout selectionSection = new() { Spacing = 8 };
    private readonly Chip undoChip, redoChip, gridChip;

    // Commands (text) panel
    private readonly ObservableCollection<string> commandTexts = new();
    private readonly CollectionView commandList;
    private readonly Grid commandsBody;
    private readonly Label commandsHeader = new() { FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Theme.Text, VerticalOptions = LayoutOptions.Center };
    private readonly Label commandsArrow = new() { FontSize = 13, TextColor = Theme.SecondaryText, VerticalOptions = LayoutOptions.Center, WidthRequest = 14 };
    private readonly Chip stepChip;
    private bool commandsExpanded;
    private bool syncingList;

    private Tool tool = Tool.Select;
    private int ink;
    private int brushSize = 1;
    private int shadePattern;
    private bool showGrid;
    private int? previewLimit;
    private int selectedIndex = -1;

    // Drag state
    private enum DragMode { None, Draw, Move, Handle }
    private DragMode dragMode;
    private (double X, double Y) dragOrigin;
    private (int X, int Y)? dragStart, dragEnd;
    private DrawCommand? dragOriginal;
    private int dragHandle = -1;
    private bool dragChanged;
    private readonly Stopwatch renderThrottle = Stopwatch.StartNew();
    private readonly List<int> pendingPoints = new();

    // Undo
    private readonly List<List<DrawCommand>> undoStack = new();
    private readonly List<List<DrawCommand>> redoStack = new();
    private bool selfChange;

    public PictureEditorView(Picture picture, EditorContext ctx)
    {
        this.picture = picture;
        this.ctx = ctx;
        ink = PictureGeometry.InkAt(picture, picture.Commands.Count);

        // ---------------- header and toolbar
        var title = new Label { Text = string.IsNullOrWhiteSpace(picture.Name) ? picture.Id : picture.Name, FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Theme.Accent };
        var headerText = new VerticalStackLayout { Spacing = 2, Children = { title, subtitle } };

        var tools = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        foreach (var group in ToolGroups)
        {
            if (tools.Children.Count > 0) tools.Children.Add(new BoxView { WidthRequest = 1, HeightRequest = 22, Color = Theme.Border, Margin = new Thickness(6, 0, 10, 4), VerticalOptions = LayoutOptions.Center });
            foreach (var (t, label, tip) in group)
            {
                var chip = new Chip(label, tip);
                chip.Clicked += (_, _) => SelectTool(t);
                toolChips[t] = chip;
                tools.Children.Add(chip);
            }
        }
        undoChip = new Chip("↶ Undo", "Undo the last change to this picture");
        undoChip.Clicked += (_, _) => Undo();
        redoChip = new Chip("↷ Redo", "Redo");
        redoChip.Clicked += (_, _) => Redo();
        gridChip = new Chip("# Grid", "Show the 8×8 character-cell grid");
        gridChip.Clicked += (_, _) => { showGrid = !showGrid; gridChip.IsSelected = showGrid; canvas!.Invalidate(); };

        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
        header.Add(headerText, 0);
        header.Add(new HorizontalStackLayout { VerticalOptions = LayoutOptions.End, Children = { undoChip, redoChip, gridChip } }, 1);
        var toolbar = tools;

        var optionsRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, MinimumHeightRequest = 30 };
        optionsRow.Add(optionsBar, 0);
        optionsRow.Add(status, 1);

        // ---------------- canvas
        drawable = new CanvasDrawable(this);
        canvas = new GraphicsView { Drawable = drawable, BackgroundColor = Theme.Canvas, MinimumHeightRequest = 300 };
        canvas.StartInteraction += (_, e) => OnStart(e.Touches[0]);
        canvas.DragInteraction += (_, e) => OnDrag(e.Touches[0]);
        canvas.EndInteraction += (_, e) => OnEnd(e.Touches.Length > 0 ? e.Touches[0] : null);
        canvas.SizeChanged += (_, _) => RefreshImage();
        var hover = new PointerGestureRecognizer();
        hover.PointerMoved += (_, e) =>
        {
            if (dragMode != DragMode.None || e.GetPosition(canvas) is not { } p) return;
            var (x, y) = ToPicture(new PointF((float)p.X, (float)p.Y));
            status.Text = x >= 0 && y >= 0 && x < picture.Width && y < picture.Height ? $"{x}, {y}" : "";
        };
        canvas.GestureRecognizers.Add(hover);
        var canvasFrame = new Border { Content = canvas, Stroke = Theme.Border, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 } };

        // ---------------- inspector
        var pictureSettings = new ObjectEditor(picture, ctx, only: new[] { "Id", "Name", "Width", "Height", "RenderMode", "InitialInk", "InitialPaper", "BitmapAsset", "IsSubroutine" },
            onStructureChanged: RefreshImage, labelWidth: 96);
        var importBackground = SmallButton("Background image…", async () => await ImportImageAsync(asBackground: true));
        ToolTipProperties.SetText(importBackground, "Import a PNG, JPEG or HEIC picture to draw over");
        var importStamp = SmallButton("Image to stamp…", async () => await ImportImageAsync(asBackground: false));
        ToolTipProperties.SetText(importStamp, "Import an image for the 🖼 Image tool");

        var inspector = new VerticalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(14, 12),
            Children =
            {
                InspectorHeading("Colour"), colourSection,
                InspectorHeading("Selection"), selectionSection,
                InspectorHeading("Picture"), pictureSettings,
                new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(0, 6, 0, 0), Children = { importBackground, importStamp } },
            },
        };
        var inspectorPane = new Border
        {
            Content = new ScrollView { Content = inspector },
            BackgroundColor = Theme.Pane,
            Stroke = Theme.Border,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            WidthRequest = 310,
        };

        // ---------------- commands panel (text form, collapsible)
        commandList = new CollectionView
        {
            ItemsSource = commandTexts,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var l = new Label { FontSize = 12, Padding = new Thickness(8, 2), FontFamily = DeviceInfo.Platform == DevicePlatform.WinUI ? "Consolas" : "Menlo", TextColor = Theme.Text };
                l.SetBinding(Label.TextProperty, ".");
                var states = new VisualStateGroup { Name = "CommonStates" };
                states.States.Add(new VisualState { Name = "Normal" });
                var selectedState = new VisualState { Name = "Selected" };
                selectedState.Setters.Add(new Setter { Property = BackgroundColorProperty, Value = Theme.Selected });
                states.States.Add(selectedState);
                VisualStateManager.GetVisualStateGroups(l).Add(states);
                return l;
            }),
        };
        commandList.SelectionChanged += (_, _) =>
        {
            if (syncingList) return;
            int i = commandList.SelectedItem is string s ? commandTexts.IndexOf(s) : -1;
            SetSelection(i, fromList: true);
            if (previewLimit != null) { previewLimit = Math.Max(1, i + 1); RefreshImage(); }
        };
        stepChip = new Chip("Step view", "Show the picture only up to the selected command");
        stepChip.Clicked += (_, _) =>
        {
            previewLimit = previewLimit == null ? Math.Max(1, selectedIndex + 1) : null;
            stepChip.IsSelected = previewLimit != null;
            RefreshImage();
        };
        var clearAll = new Chip("Clear all", "Remove every drawing command");
        clearAll.Clicked += async (_, _) =>
        {
            if (!await ctx.PageProvider().DisplayAlertAsync("Clear picture", "Remove all drawing commands?", "Clear", "Cancel")) return;
            Snapshot();
            picture.Commands.Clear();
            selectedIndex = -1;
            Changed();
        };
        var up = new Chip("↑ Earlier", "Move the selected command earlier (drawn underneath)");
        up.Clicked += (_, _) => MoveSelected(-1);
        var down = new Chip("↓ Later", "Move the selected command later (drawn on top)");
        down.Clicked += (_, _) => MoveSelected(1);
        var del = new Chip("Delete", "Delete the selected command");
        del.Clicked += (_, _) => DeleteSelected();
        var commandButtons = new VerticalStackLayout { Spacing = 0, Children = { up, down, del, stepChip, clearAll } };
        commandsBody = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10, HeightRequest = 210, Padding = new Thickness(10, 0, 10, 10) };
        commandsBody.Add(new Border { Content = commandList, Stroke = Theme.Border, StrokeThickness = 1, BackgroundColor = Theme.Pane }, 0);
        commandsBody.Add(commandButtons, 1);

        var commandsToggle = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6, Padding = new Thickness(10, 8) };
        commandsToggle.Add(commandsArrow, 0);
        commandsToggle.Add(commandsHeader, 1);
        commandsToggle.Add(new Label { Text = "the picture as text: every shape is one command, drawn in order", FontSize = 11, TextColor = Theme.SecondaryText, VerticalOptions = LayoutOptions.Center }, 2);
        commandsToggle.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SetCommandsExpanded(!commandsExpanded, animate: true)) });
        ToolTipProperties.SetText(commandsToggle, "Show or hide the drawing commands");
        var commandsPanel = new Border
        {
            Content = new VerticalStackLayout { Children = { commandsToggle, commandsBody } },
            BackgroundColor = Theme.Sidebar,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        };

        // ---------------- layout
        var root = new Grid
        {
            ClassId = "full",
            Padding = new Thickness(20, 14, 20, 14),
            RowSpacing = 8,
            ColumnSpacing = 14,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
        };
        root.Add(header, 0, 0); Grid.SetColumnSpan(header, 2);
        root.Add(toolbar, 0, 1); Grid.SetColumnSpan(toolbar, 2);
        root.Add(optionsRow, 0, 2);
        root.Add(canvasFrame, 0, 3);
        root.Add(inspectorPane, 1, 2); Grid.SetRowSpan(inspectorPane, 2);
        root.Add(commandsPanel, 0, 4); Grid.SetColumnSpan(commandsPanel, 2);
        Content = root;

        // Picture settings edited in the inspector change the document; redraw when that happens.
        void OnDocumentChanged(object? what)
        {
            if (!selfChange && ReferenceEquals(what, picture)) { UpdateSubtitle(); RefreshImage(); BuildColourSection(); }
        }
        Loaded += (_, _) => ctx.Document.Changed += OnDocumentChanged;
        Unloaded += (_, _) => ctx.Document.Changed -= OnDocumentChanged;

        bool expanded = false;
        try { expanded = Preferences.Default.Get(CommandsExpandedKey, false); } catch { }
        SetCommandsExpanded(expanded, animate: false);
        UpdateSubtitle();
        RefreshList();
        BuildColourSection();
        BuildSelectionSection();
        SelectTool(Tool.Select);
        UpdateUndoButtons();
    }

    // =================================================================== layout helpers

    private static View InspectorHeading(string text) => new VerticalStackLayout
    {
        Spacing = 4,
        Margin = new Thickness(0, 10, 0, 2),
        Children =
        {
            new Label { Text = text.ToUpperInvariant(), FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Theme.SecondaryText, CharacterSpacing = 0.8 },
            new BoxView { HeightRequest = 1, Color = Theme.Border },
        },
    };

    private static Button SmallButton(string text, Action action)
    {
        var b = new Button { Text = text, FontSize = 12, HeightRequest = 28, Padding = new Thickness(10, 0), Margin = new Thickness(0, 0, 6, 6) };
        b.Clicked += (_, _) => action();
        return b;
    }

    private void UpdateSubtitle() =>
        subtitle.Text = $"Picture · {picture.Id} · {picture.Width}×{picture.Height} · " +
                        (picture.RenderMode == PictureRenderMode.SpectrumAttributes ? "Spectrum attributes" : "full colour") +
                        (picture.IsSubroutine ? " · sub-picture" : "");

    private void SetCommandsExpanded(bool expanded, bool animate)
    {
        commandsExpanded = expanded;
        commandsArrow.Text = expanded ? "▾" : "▸";
        try { Preferences.Default.Set(CommandsExpandedKey, expanded); } catch { }
        if (!animate)
        {
            commandsBody.IsVisible = expanded;
            commandsBody.HeightRequest = 210;
            return;
        }
        this.AbortAnimation("commands");
        if (expanded) { commandsBody.HeightRequest = 0; commandsBody.IsVisible = true; }
        double from = expanded ? 0 : 210, to = expanded ? 210 : 0;
        new Animation(v => commandsBody.HeightRequest = v, from, to, Easing.CubicInOut)
            .Commit(this, "commands", length: 180, finished: (_, _) =>
            {
                commandsBody.IsVisible = commandsExpanded;
                commandsBody.HeightRequest = 210;
            });
    }

    // =================================================================== colour

    private void BuildColourSection()
    {
        colourSection.Children.Clear();
        var current = new Border
        {
            WidthRequest = 30, HeightRequest = 30,
            BackgroundColor = Colour(ink), Stroke = Theme.Border, StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
        };
        var info = new Label
        {
            Text = selectedIndex >= 0 && PictureGeometry.IsVisual(picture.Commands[selectedIndex].Op)
                ? $"Ink {ink}\nClick a colour to recolour the selection"
                : $"Ink {ink}\nNew shapes are drawn in this colour",
            FontSize = 12, TextColor = Theme.SecondaryText, VerticalOptions = LayoutOptions.Center,
        };
        colourSection.Children.Add(new HorizontalStackLayout { Spacing = 10, Children = { current, info } });

        var swatches = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        for (int i = 0; i < picture.Palette.Count; i++)
        {
            int index = i;
            var swatch = new Border
            {
                WidthRequest = 24, HeightRequest = 24,
                Margin = new Thickness(0, 0, 4, 4),
                BackgroundColor = Colour(i),
                StrokeThickness = index == ink ? 3 : 1,
                Stroke = index == ink ? Theme.Accent : Theme.Border,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
            };
            ToolTipProperties.SetText(swatch, $"Colour {index}");
            swatch.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => PickColour(index)) });
            swatches.Children.Add(swatch);
        }
        colourSection.Children.Add(swatches);

        var background = SmallButton("Use as background", () =>
        {
            Snapshot();
            if (picture.Commands.Count > 0 && picture.Commands[0].Op == DrawOp.Clear) picture.Commands[0].Color = ink;
            else { picture.Commands.Insert(0, new DrawCommand { Op = DrawOp.Clear, Color = ink }); if (selectedIndex >= 0) selectedIndex++; }
            picture.InitialPaper = ink;
            Changed();
        });
        ToolTipProperties.SetText(background, "Fill the whole picture with this colour (a Clear command at the start)");
        colourSection.Children.Add(background);
    }

    private Color Colour(int index) => index >= 0 && index < picture.Palette.Count ? Maui.PictureImages.FromArgb(picture.Palette[index]) : Colors.Transparent;

    private void PickColour(int index)
    {
        ink = index;
        if (selectedIndex >= 0 && selectedIndex < picture.Commands.Count)
        {
            var c = picture.Commands[selectedIndex];
            if (PictureGeometry.UsesInk(c.Op) || c.Op == DrawOp.AttributeBlock)
            {
                if (PictureGeometry.InkAt(picture, selectedIndex) != index || c.Op == DrawOp.AttributeBlock)
                {
                    Snapshot();
                    selectedIndex = PictureGeometry.Recolour(picture, selectedIndex, index);
                    Changed();
                    return;
                }
            }
            else if (c.Op is DrawOp.SetInk or DrawOp.SetPaper or DrawOp.Clear)
            {
                Snapshot();
                c.Color = index;
                Changed();
                return;
            }
        }
        BuildColourSection();
        canvas.Invalidate();
    }

    // =================================================================== selection inspector

    private void BuildSelectionSection()
    {
        selectionSection.Children.Clear();
        if (selectedIndex < 0 || selectedIndex >= picture.Commands.Count)
        {
            selectionSection.Children.Add(new Label
            {
                Text = "Nothing selected. Use ↖ Select and click a shape on the picture to move, reshape, recolour or delete it.",
                FontSize = 12, TextColor = Theme.SecondaryText,
            });
            return;
        }
        var c = picture.Commands[selectedIndex];
        selectionSection.Children.Add(new Label
        {
            Text = $"{PictureGeometry.Describe(c.Op)}  ·  command {selectedIndex + 1} of {picture.Commands.Count}",
            FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Theme.Text,
        });

        var fields = new Grid { ColumnDefinitions = { new(new GridLength(44)), new(GridLength.Star), new(new GridLength(44)), new(GridLength.Star) }, ColumnSpacing = 6, RowSpacing = 6 };
        int row = 0;
        void Pair(string l1, Func<int> g1, Action<int> s1, string? l2 = null, Func<int>? g2 = null, Action<int>? s2 = null)
        {
            fields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            fields.Add(FieldLabel(l1), 0, row);
            fields.Add(NumberEntry(g1, s1), 1, row);
            if (l2 != null && g2 != null && s2 != null)
            {
                fields.Add(FieldLabel(l2), 2, row);
                fields.Add(NumberEntry(g2, s2), 3, row);
            }
            row++;
        }

        switch (c.Op)
        {
            case DrawOp.Line:
                Pair("From X", () => c.X, v => c.X = v, "Y", () => c.Y, v => c.Y = v);
                Pair("To X", () => c.X2, v => c.X2 = v, "Y", () => c.Y2, v => c.Y2 = v);
                break;
            case DrawOp.Rectangle or DrawOp.FilledRectangle or DrawOp.Ellipse or DrawOp.FilledEllipse or DrawOp.AttributeBlock:
                Pair("Left", () => Math.Min(c.X, c.X2), v => { int w = Math.Abs(c.X2 - c.X); c.X = v; c.X2 = v + w; },
                     "Top", () => Math.Min(c.Y, c.Y2), v => { int h = Math.Abs(c.Y2 - c.Y); c.Y = v; c.Y2 = v + h; });
                Pair("Width", () => Math.Abs(c.X2 - c.X), v => { c.X = Math.Min(c.X, c.X2); c.X2 = c.X + Math.Max(0, v); },
                     "Height", () => Math.Abs(c.Y2 - c.Y), v => { c.Y = Math.Min(c.Y, c.Y2); c.Y2 = c.Y + Math.Max(0, v); });
                break;
            case DrawOp.Plot or DrawOp.Fill or DrawOp.Shade or DrawOp.Call:
                Pair("X", () => c.X, v => c.X = v, "Y", () => c.Y, v => c.Y = v);
                if (c.Op == DrawOp.Call) Pair("Scale", () => c.Scale, v => c.Scale = Math.Max(1, v));
                break;
            case DrawOp.Text:
                Pair("X", () => c.X, v => c.X = v, "Y", () => c.Y, v => c.Y = v);
                Pair("Size", () => c.Scale == 8 ? 1 : c.Scale, v => c.Scale = Math.Clamp(v, 1, 7));
                break;
            case DrawOp.Image:
                Pair("X", () => c.X, v => c.X = v, "Y", () => c.Y, v => c.Y = v);
                Pair("Width", () => PictureGeometry.ImageSize(c, ctx.Adventure).W, v => c.X2 = Math.Max(1, v),
                     "Height", () => PictureGeometry.ImageSize(c, ctx.Adventure).H, v => c.Y2 = Math.Max(1, v));
                break;
            case DrawOp.Freehand:
                Pair("Brush", () => c.Scale, v => c.Scale = Math.Clamp(v, 1, 32));
                break;
            case DrawOp.SetBright:
                Pair("Bright", () => c.X, v => c.X = v == 0 ? 0 : 1);
                break;
        }
        if (row > 0) selectionSection.Children.Add(fields);

        if (c.Op == DrawOp.Text)
        {
            var text = new Entry { Text = c.Text, FontSize = 13, Placeholder = "Text" };
            void Commit() { if (text.Text != c.Text) { Snapshot(); c.Text = text.Text; Changed(); } }
            text.Completed += (_, _) => Commit();
            text.Unfocused += (_, _) => Commit();
            selectionSection.Children.Add(text);
        }
        if (c.Op is DrawOp.Polygon or DrawOp.FilledPolygon or DrawOp.Freehand)
            selectionSection.Children.Add(new Label { Text = $"{c.Points.Count / 2} points. Drag the shape to move it" + (c.Op == DrawOp.Freehand ? "." : ", or drag a point to reshape it."), FontSize = 12, TextColor = Theme.SecondaryText });
        if (c.Op == DrawOp.Shade)
            selectionSection.Children.Add(PatternPicker(c.Pattern, p => { Snapshot(); c.Pattern = p.ToArray(); Changed(); }));
        if (c.Op == DrawOp.Call)
            selectionSection.Children.Add(new Label { Text = $"Draws “{ctx.Adventure.FindPicture(c.SubPictureId)?.Name ?? c.SubPictureId}”. Scale is in eighths (8 = actual size).", FontSize = 12, TextColor = Theme.SecondaryText });

        var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        void Act(string label, string tip, Action a) { var chip = new Chip(label, tip); chip.Clicked += (_, _) => a(); actions.Children.Add(chip); }
        Act("Duplicate", "Copy the shape, offset slightly", Duplicate);
        Act("To front", "Draw last, on top of everything", () => MoveSelectedTo(picture.Commands.Count - 1));
        Act("To back", "Draw first, underneath everything", () => MoveSelectedTo(picture.Commands.Count > 0 && picture.Commands[0].Op == DrawOp.Clear ? 1 : 0));
        Act("Delete", "Delete the shape", DeleteSelected);
        selectionSection.Children.Add(actions);
    }

    private static Label FieldLabel(string text) => new() { Text = text, FontSize = 12, TextColor = Theme.SecondaryText, VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.End };

    private Entry NumberEntry(Func<int> get, Action<int> set)
    {
        var e = new Entry { Text = get().ToString(), Keyboard = Keyboard.Numeric, FontSize = 13, HorizontalTextAlignment = TextAlignment.End };
        void Commit()
        {
            if (!int.TryParse(e.Text, out var v)) { e.Text = get().ToString(); return; }
            if (v == get()) return;
            Snapshot();
            set(v);
            Changed();
        }
        e.Completed += (_, _) => Commit();
        e.Unfocused += (_, _) => Commit();
        return e;
    }

    private View PatternPicker(byte[]? current, Action<byte[]> choose)
    {
        var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        for (int i = 0; i < ShadePatterns.Length; i++)
        {
            var pattern = ShadePatterns[i];
            bool selected = current != null ? current.SequenceEqual(pattern) : i == shadePattern;
            var view = new GraphicsView { Drawable = new PatternDrawable(pattern, Colour(ink)), WidthRequest = 24, HeightRequest = 24 };
            var frame = new Border
            {
                Content = view, Padding = 1, Margin = new Thickness(0, 0, 4, 4),
                Stroke = selected ? Theme.Accent : Theme.Border, StrokeThickness = selected ? 3 : 1, BackgroundColor = Colors.White,
            };
            ToolTipProperties.SetText(frame, $"Pattern {i + 1}");
            frame.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => choose(pattern)) });
            row.Children.Add(frame);
        }
        return row;
    }

    /// <summary>Selects drawing command <paramref name="index"/> (-1 for none).</summary>
    public void SelectCommand(int index) => SetSelection(index);

    private void SetSelection(int index, bool fromList = false)
    {
        selectedIndex = index >= 0 && index < picture.Commands.Count ? index : -1;
        // The colour panel shows the selected shape's colour.
        if (selectedIndex >= 0 && picture.Commands[selectedIndex] is var c)
        {
            if (PictureGeometry.UsesInk(c.Op)) ink = PictureGeometry.InkAt(picture, selectedIndex);
            else if (c.Op is DrawOp.AttributeBlock or DrawOp.SetInk or DrawOp.SetPaper or DrawOp.Clear) ink = c.Color;
        }
        if (!fromList) SyncListSelection();
        BuildSelectionSection();
        BuildColourSection();
        canvas.Invalidate();
    }

    private void SyncListSelection()
    {
        syncingList = true;
        commandList.SelectedItem = selectedIndex >= 0 && selectedIndex < commandTexts.Count ? commandTexts[selectedIndex] : null;
        if (selectedIndex >= 0 && selectedIndex < commandTexts.Count && commandsExpanded)
        {
            // After layout, so it also works while the editor is first appearing.
            int index = selectedIndex;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
            {
                if (index < commandTexts.Count) commandList.ScrollTo(index, position: ScrollToPosition.Center, animate: false);
            });
        }
        syncingList = false;
    }

    // =================================================================== editing operations

    private void Snapshot()
    {
        undoStack.Add(picture.Commands.Select(c => c.Clone()).ToList());
        if (undoStack.Count > 100) undoStack.RemoveAt(0);
        redoStack.Clear();
        UpdateUndoButtons();
    }

    private void Undo()
    {
        if (undoStack.Count == 0) return;
        redoStack.Add(picture.Commands.Select(c => c.Clone()).ToList());
        picture.Commands = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        if (selectedIndex >= picture.Commands.Count) selectedIndex = -1;
        Changed();
    }

    private void Redo()
    {
        if (redoStack.Count == 0) return;
        undoStack.Add(picture.Commands.Select(c => c.Clone()).ToList());
        picture.Commands = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        if (selectedIndex >= picture.Commands.Count) selectedIndex = -1;
        Changed();
    }

    private void UpdateUndoButtons()
    {
        undoChip.Opacity = undoStack.Count > 0 ? 1 : 0.45;
        redoChip.Opacity = redoStack.Count > 0 ? 1 : 0.45;
    }

    private void DeleteSelected()
    {
        if (selectedIndex < 0) return;
        Snapshot();
        picture.Commands.RemoveAt(selectedIndex);
        selectedIndex = -1;
        Changed();
    }

    private void Duplicate()
    {
        if (selectedIndex < 0) return;
        Snapshot();
        var copy = picture.Commands[selectedIndex].Clone();
        if (PictureGeometry.IsVisual(copy.Op)) PictureGeometry.Translate(copy, 4, 4);
        picture.Commands.Insert(selectedIndex + 1, copy);
        selectedIndex++;
        Changed();
    }

    private void MoveSelected(int delta) => MoveSelectedTo(selectedIndex + delta);

    private void MoveSelectedTo(int to)
    {
        int i = selectedIndex;
        if (i < 0 || to < 0 || to >= picture.Commands.Count || to == i) return;
        Snapshot();
        var c = picture.Commands[i];
        int colour = PictureGeometry.InkAt(picture, i);
        picture.Commands.RemoveAt(i);
        picture.Commands.Insert(to, c);
        selectedIndex = to;
        // Keep the shape's colour when it moves past SetInk commands.
        if (PictureGeometry.UsesInk(c.Op)) selectedIndex = PictureGeometry.Recolour(picture, to, colour);
        Changed();
    }

    private void Changed()
    {
        selfChange = true;
        try { ctx.Changed(picture); }
        finally { selfChange = false; }
        RefreshList();
        RefreshImage();
        BuildSelectionSection();
        BuildColourSection();
        UpdateUndoButtons();
    }

    private void RefreshList()
    {
        syncingList = true;
        commandTexts.Clear();
        for (int i = 0; i < picture.Commands.Count; i++) commandTexts.Add($"{i + 1,4}  {picture.Commands[i]}");
        syncingList = false;
        commandsHeader.Text = $"Drawing commands ({picture.Commands.Count})";
        SyncListSelection();
    }

    private void SelectTool(Tool t)
    {
        tool = t;
        pendingPoints.Clear();
        foreach (var (k, chip) in toolChips) chip.IsSelected = k == t;
        status.Text = ToolGroups.SelectMany(g => g).First(x => x.Tool == t).Tip;
        BuildOptions();
        canvas.Invalidate();
    }

    /// <summary>Tool options that apply to the current tool: brush size, fill pattern, finishing a shape.</summary>
    private void BuildOptions()
    {
        optionsBar.Children.Clear();
        if (tool is Tool.Freehand or Tool.Text)
        {
            var label = new Label { Text = tool == Tool.Text ? $"Text size {brushSize}" : $"Brush {brushSize}", FontSize = 12, VerticalOptions = LayoutOptions.Center, TextColor = Theme.Text };
            var stepper = new Stepper { Minimum = 1, Maximum = tool == Tool.Text ? 7 : 12, Value = Math.Min(brushSize, tool == Tool.Text ? 7 : 12), Increment = 1, VerticalOptions = LayoutOptions.Center };
            stepper.ValueChanged += (_, e) => { brushSize = (int)e.NewValue; label.Text = tool == Tool.Text ? $"Text size {brushSize}" : $"Brush {brushSize}"; };
            optionsBar.Children.Add(label);
            optionsBar.Children.Add(stepper);
        }
        if (tool == Tool.Shade)
        {
            optionsBar.Children.Add(new Label { Text = "Pattern", FontSize = 12, VerticalOptions = LayoutOptions.Center, TextColor = Theme.Text });
            optionsBar.Children.Add(PatternPicker(ShadePatterns[shadePattern], p => { shadePattern = Array.IndexOf(ShadePatterns, p); BuildOptions(); }));
        }
        if (tool is Tool.Polygon or Tool.FilledPolygon && pendingPoints.Count >= 6)
        {
            var finish = new Chip("✓ Finish shape", "Close the shape");
            finish.Clicked += (_, _) => FinishPolygon();
            var cancel = new Chip("Cancel", "Discard the points");
            cancel.Clicked += (_, _) => { pendingPoints.Clear(); BuildOptions(); canvas.Invalidate(); };
            optionsBar.Children.Add(finish);
            optionsBar.Children.Add(cancel);
        }
    }

    // =================================================================== rendering

    internal IImage? Image;
    internal int Scale = 1;
    internal float OffsetX, OffsetY;

    private void RefreshImage()
    {
        if (canvas.Width <= 0 || canvas.Height <= 0) return;
        Scale = Math.Max(1, (int)Math.Min((canvas.Width - 16) / picture.Width, (canvas.Height - 16) / picture.Height));
        try
        {
            var png = PictureRenderer.RenderPng(picture, ctx.Adventure, Scale, previewLimit);
            Image = PlatformImage.FromStream(new MemoryStream(png));
        }
        catch (Exception ex)
        {
            status.Text = "Render error: " + ex.Message;
        }
        renderThrottle.Restart();
        canvas.Invalidate();
    }

    private (double X, double Y) ToPictureExact(PointF p) => ((p.X - OffsetX) / Scale, (p.Y - OffsetY) / Scale);

    private (int X, int Y) ToPicture(PointF p)
    {
        var (x, y) = ToPictureExact(p);
        return ((int)Math.Floor(x), (int)Math.Floor(y));
    }

    private void EnsureInk()
    {
        if (PictureGeometry.InkAt(picture, picture.Commands.Count) != ink) picture.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = ink });
    }

    private double Tolerance => Math.Max(1.5, 5.0 / Scale);

    // =================================================================== interaction

    private void OnStart(PointF p)
    {
        var pt = ToPicture(p);
        var exact = ToPictureExact(p);
        dragChanged = false;
        if (tool == Tool.Select)
        {
            dragOrigin = exact;
            if (selectedIndex >= 0 && selectedIndex < picture.Commands.Count)
            {
                int handle = PictureGeometry.HandleAt(picture.Commands[selectedIndex], ctx.Adventure, exact.X, exact.Y, Math.Max(2, 6.0 / Scale));
                if (handle >= 0)
                {
                    dragMode = DragMode.Handle;
                    dragHandle = handle;
                    dragOriginal = picture.Commands[selectedIndex].Clone();
                    return;
                }
            }
            int hit = PictureGeometry.HitTest(picture, ctx.Adventure, exact.X, exact.Y, Tolerance);
            if (previewLimit != null && hit >= previewLimit) hit = -1;
            SetSelection(hit);
            if (hit >= 0)
            {
                dragMode = DragMode.Move;
                dragOriginal = picture.Commands[hit].Clone();
                status.Text = $"{PictureGeometry.Describe(picture.Commands[hit].Op)} · drag to move";
            }
            else dragMode = DragMode.None;
            return;
        }

        dragMode = DragMode.Draw;
        dragStart = pt;
        dragEnd = pt;
        if (tool == Tool.Freehand) { pendingPoints.Clear(); pendingPoints.Add(pt.X); pendingPoints.Add(pt.Y); }
        status.Text = $"{pt.X}, {pt.Y}";
        canvas.Invalidate();
    }

    private void OnDrag(PointF p)
    {
        var pt = ToPicture(p);
        var exact = ToPictureExact(p);
        switch (dragMode)
        {
            case DragMode.Move when dragOriginal != null && selectedIndex >= 0:
            {
                int dx = (int)Math.Round(exact.X - dragOrigin.X), dy = (int)Math.Round(exact.Y - dragOrigin.Y);
                if (dx == 0 && dy == 0 && !dragChanged) return;
                if (!dragChanged) { Snapshot(); dragChanged = true; }
                var moved = dragOriginal.Clone();
                PictureGeometry.Translate(moved, dx, dy);
                picture.Commands[selectedIndex] = moved;
                status.Text = $"Move {dx:+0;-0;0}, {dy:+0;-0;0}";
                LiveRender();
                return;
            }
            case DragMode.Handle when dragOriginal != null && selectedIndex >= 0:
            {
                if (!dragChanged) { Snapshot(); dragChanged = true; }
                var reshaped = dragOriginal.Clone();
                PictureGeometry.MoveHandle(reshaped, dragHandle, pt.X, pt.Y, ctx.Adventure);
                picture.Commands[selectedIndex] = reshaped;
                status.Text = $"{pt.X}, {pt.Y}";
                LiveRender();
                return;
            }
            case DragMode.Draw:
                dragEnd = pt;
                if (tool == Tool.Freehand && (pendingPoints.Count < 2 || pendingPoints[^2] != pt.X || pendingPoints[^1] != pt.Y))
                {
                    pendingPoints.Add(pt.X);
                    pendingPoints.Add(pt.Y);
                }
                status.Text = dragStart is { } s ? $"{s.X}, {s.Y} → {pt.X}, {pt.Y}  ({Math.Abs(pt.X - s.X) + 1}×{Math.Abs(pt.Y - s.Y) + 1})" : $"{pt.X}, {pt.Y}";
                canvas.Invalidate();
                return;
        }
    }

    /// <summary>Redraws while dragging, at most about 30 times a second.</summary>
    private void LiveRender()
    {
        if (renderThrottle.ElapsedMilliseconds >= 33) RefreshImage();
        else canvas.Invalidate();
    }

    private async void OnEnd(PointF? p)
    {
        var mode = dragMode;
        dragMode = DragMode.None;
        if (mode is DragMode.Move or DragMode.Handle)
        {
            dragOriginal = null;
            if (dragChanged) Changed();
            return;
        }
        if (mode != DragMode.Draw) return;

        if (p is { } end) dragEnd = ToPicture(end);
        if (dragStart is not { } a || dragEnd is not { } b) return;
        dragStart = null;
        dragEnd = null;

        switch (tool)
        {
            case Tool.Line:
            case Tool.Rectangle:
            case Tool.FilledRectangle:
            case Tool.Ellipse:
            case Tool.FilledEllipse:
                if (a == b && tool != Tool.Line) { canvas.Invalidate(); return; }
                Snapshot();
                EnsureInk();
                picture.Commands.Add(new DrawCommand
                {
                    Op = tool switch
                    {
                        Tool.Line => DrawOp.Line, Tool.Rectangle => DrawOp.Rectangle, Tool.FilledRectangle => DrawOp.FilledRectangle,
                        Tool.Ellipse => DrawOp.Ellipse, _ => DrawOp.FilledEllipse,
                    },
                    X = a.X, Y = a.Y, X2 = b.X, Y2 = b.Y,
                });
                break;
            case Tool.Block:
                Snapshot();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.AttributeBlock, X = a.X, Y = a.Y, X2 = b.X, Y2 = b.Y, Color = ink, Color2 = -1 });
                break;
            case Tool.Plot:
                Snapshot();
                EnsureInk();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = b.X, Y = b.Y });
                break;
            case Tool.Fill:
                Snapshot();
                EnsureInk();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Fill, X = b.X, Y = b.Y });
                break;
            case Tool.Shade:
                Snapshot();
                EnsureInk();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Shade, X = b.X, Y = b.Y, Pattern = ShadePatterns[shadePattern].ToArray() });
                break;
            case Tool.Freehand:
                if (pendingPoints.Count >= 2)
                {
                    Snapshot();
                    EnsureInk();
                    picture.Commands.Add(new DrawCommand { Op = DrawOp.Freehand, Points = pendingPoints.ToList(), Scale = brushSize });
                }
                pendingPoints.Clear();
                break;
            case Tool.Polygon:
            case Tool.FilledPolygon:
                if (pendingPoints.Count >= 6 && Math.Abs(b.X - pendingPoints[0]) <= 3 && Math.Abs(b.Y - pendingPoints[1]) <= 3)
                {
                    FinishPolygon();
                    return;
                }
                pendingPoints.Add(b.X);
                pendingPoints.Add(b.Y);
                BuildOptions();
                canvas.Invalidate();
                return;
            case Tool.Text:
            {
                var text = await ctx.PageProvider().DisplayPromptAsync("Text", "Text to draw:");
                if (string.IsNullOrEmpty(text)) return;
                Snapshot();
                EnsureInk();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Text, X = b.X, Y = b.Y, Text = text, Scale = brushSize });
                break;
            }
            case Tool.Call:
            {
                var subs = ctx.Adventure.Pictures.Where(x => x != picture).ToList();
                if (subs.Count == 0) { await ctx.PageProvider().DisplayAlertAsync("No pictures", "Create another picture to use as a sub-picture first.", "OK"); return; }
                var choice = await ctx.PageProvider().DisplayActionSheetAsync("Sub-picture", "Cancel", null, subs.Select(s => $"{s.Name} ({s.Id})").ToArray());
                var sub = subs.FirstOrDefault(s => $"{s.Name} ({s.Id})" == choice);
                if (sub == null) return;
                Snapshot();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Call, SubPictureId = sub.Id, X = b.X, Y = b.Y, Scale = 8 });
                break;
            }
            case Tool.Stamp:
            {
                var images = ctx.Adventure.Assets.Keys.Where(k => k.StartsWith("images/", StringComparison.OrdinalIgnoreCase)).ToList();
                if (images.Count == 0) { await ctx.PageProvider().DisplayAlertAsync("No images", "Use \"Image to stamp…\" in the Picture settings first.", "OK"); return; }
                var choice = await ctx.PageProvider().DisplayActionSheetAsync("Image", "Cancel", null, images.ToArray());
                if (choice == null || choice == "Cancel") return;
                int w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
                Snapshot();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Image, Text = choice, X = Math.Min(a.X, b.X), Y = Math.Min(a.Y, b.Y), X2 = w > 2 ? w : 0, Y2 = h > 2 ? h : 0 });
                break;
            }
        }
        // The new shape becomes the selection, so it can be adjusted straight away in the inspector.
        selectedIndex = picture.Commands.Count - 1;
        Changed();
    }

    private void FinishPolygon()
    {
        if (pendingPoints.Count >= 4)
        {
            Snapshot();
            EnsureInk();
            picture.Commands.Add(new DrawCommand { Op = tool == Tool.FilledPolygon ? DrawOp.FilledPolygon : DrawOp.Polygon, Points = pendingPoints.ToList() });
            selectedIndex = picture.Commands.Count - 1;
        }
        pendingPoints.Clear();
        BuildOptions();
        Changed();
    }

    private async Task ImportImageAsync(bool asBackground)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an image", FileTypes = FilePickerFileType.Images });
            if (file == null) return;
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (!RasterImage.IsPng(bytes) || !CanDecode(bytes))
            {
                // Convert JPEG/HEIC/GIF/… to PNG with the platform codecs.
                var img = PlatformImage.FromStream(new MemoryStream(bytes));
                if (img.Width > 1024) img = img.Downsize(1024);
                using var outMs = new MemoryStream();
                await img.SaveAsync(outMs, ImageFormat.Png);
                bytes = outMs.ToArray();
            }
            var name = "images/" + Path.GetFileNameWithoutExtension(file.FileName) + ".png";
            ctx.Adventure.Assets[name] = bytes;
            if (asBackground)
            {
                picture.BitmapAsset = name;
                picture.RenderMode = PictureRenderMode.FullColour;
                var decoded = RasterImage.DecodePng(bytes);
                if (picture.Commands.Count == 0)
                {
                    picture.Width = Math.Min(1024, decoded.Width);
                    picture.Height = (int)((long)decoded.Height * picture.Width / decoded.Width);
                }
            }
            Changed();
            UpdateSubtitle();
            status.Text = $"Imported {name}";
        }
        catch (Exception ex)
        {
            await ctx.PageProvider().DisplayAlertAsync("Import failed", ex.Message, "OK");
        }
    }

    private static bool CanDecode(byte[] png)
    {
        try { RasterImage.DecodePng(png); return true; }
        catch { return false; }
    }

    // =================================================================== drawables

    private sealed class PatternDrawable(byte[] pattern, Color ink) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            float cell = Math.Max(1, (float)Math.Floor(Math.Min(rect.Width, rect.Height) / 8));
            canvas.FillColor = ink.GetLuminosity() > 0.85f ? Colors.Black : ink;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (((pattern[y] >> (7 - x)) & 1) != 0) canvas.FillRectangle(x * cell, y * cell, cell, cell);
        }
    }

    private sealed class CanvasDrawable(PictureEditorView view) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var pic = view.picture;
            int s = view.Scale;
            float w = pic.Width * s, h = pic.Height * s;
            view.OffsetX = (float)Math.Floor((rect.Width - w) / 2);
            view.OffsetY = (float)Math.Floor((rect.Height - h) / 2);
            float ox = view.OffsetX, oy = view.OffsetY;

            canvas.Antialias = false;
            // Drop shadow and the picture itself.
            canvas.FillColor = Colors.Black.WithAlpha(0.12f);
            canvas.FillRectangle(ox + 3, oy + 3, w, h);
            if (view.Image != null) canvas.DrawImage(view.Image, ox, oy, w, h);
            canvas.StrokeColor = Colors.Black.WithAlpha(0.35f);
            canvas.StrokeSize = 1;
            canvas.DrawRectangle(ox - 0.5f, oy - 0.5f, w + 1, h + 1);

            if (view.showGrid && s >= 2)
            {
                canvas.StrokeColor = Colors.White.WithAlpha(0.35f);
                for (int x = 8; x < pic.Width; x += 8) canvas.DrawLine(ox + x * s, oy, ox + x * s, oy + h);
                for (int y = 8; y < pic.Height; y += 8) canvas.DrawLine(ox, oy + y * s, ox + w, oy + y * s);
            }

            PointF P(int x, int y) => new(ox + x * s + s / 2f, oy + y * s + s / 2f);

            // Selection outline and handles.
            if (view.selectedIndex >= 0 && view.selectedIndex < pic.Commands.Count)
            {
                var c = pic.Commands[view.selectedIndex];
                if (PictureGeometry.Bounds(c, view.ctx.Adventure) is { } b)
                {
                    float x0 = ox + b.X0 * s - 3, y0 = oy + b.Y0 * s - 3, x1 = ox + (b.X1 + 1) * s + 3, y1 = oy + (b.Y1 + 1) * s + 3;
                    canvas.StrokeSize = 1;
                    canvas.StrokeColor = Colors.White;
                    canvas.DrawRectangle(x0, y0, x1 - x0, y1 - y0);
                    canvas.StrokeColor = Theme.Accent;
                    canvas.StrokeDashPattern = new float[] { 4, 3 };
                    canvas.DrawRectangle(x0, y0, x1 - x0, y1 - y0);
                    canvas.StrokeDashPattern = null;
                    if (c.Op is DrawOp.Plot or DrawOp.Fill or DrawOp.Shade)
                    {
                        var centre = P(c.X, c.Y);
                        canvas.StrokeColor = Theme.Accent;
                        canvas.StrokeSize = 2;
                        canvas.DrawLine(centre.X - 8, centre.Y, centre.X + 8, centre.Y);
                        canvas.DrawLine(centre.X, centre.Y - 8, centre.X, centre.Y + 8);
                    }
                }
                if (view.tool == Tool.Select)
                {
                    canvas.StrokeSize = 1.5f;
                    foreach (var (hx, hy) in PictureGeometry.Handles(c, view.ctx.Adventure))
                    {
                        var hp = P(hx, hy);
                        canvas.FillColor = Colors.White;
                        canvas.FillRectangle(hp.X - 4, hp.Y - 4, 8, 8);
                        canvas.StrokeColor = Theme.Accent;
                        canvas.DrawRectangle(hp.X - 4, hp.Y - 4, 8, 8);
                    }
                }
            }

            // Previews of the shape being drawn, in the ink colour.
            var inkColour = view.Colour(view.ink);
            canvas.StrokeColor = inkColour;
            canvas.StrokeSize = Math.Max(1, s);
            canvas.StrokeDashPattern = null;

            if (view.dragMode == DragMode.Draw && view.dragStart is { } a && view.dragEnd is { } e)
            {
                var tl = P(Math.Min(a.X, e.X), Math.Min(a.Y, e.Y));
                var br = P(Math.Max(a.X, e.X), Math.Max(a.Y, e.Y));
                var box = RectF.FromLTRB(tl.X, tl.Y, br.X, br.Y);
                canvas.FillColor = inkColour;
                switch (view.tool)
                {
                    case Tool.Line: canvas.DrawLine(P(a.X, a.Y), P(e.X, e.Y)); break;
                    case Tool.Rectangle: canvas.DrawRectangle(box); break;
                    case Tool.FilledRectangle: canvas.FillRectangle(box); break;
                    case Tool.Ellipse: canvas.DrawEllipse(box); break;
                    case Tool.FilledEllipse: canvas.FillEllipse(box); break;
                    case Tool.Block:
                    case Tool.Stamp:
                        canvas.StrokeSize = 1;
                        canvas.StrokeDashPattern = new float[] { 4, 3 };
                        canvas.DrawRectangle(box);
                        canvas.StrokeDashPattern = null;
                        break;
                }
            }
            var pts = view.pendingPoints;
            if (pts.Count >= 2)
            {
                if (view.tool == Tool.Freehand) canvas.StrokeSize = Math.Max(1, s * view.brushSize);
                for (int i = 0; i + 3 < pts.Count; i += 2) canvas.DrawLine(P(pts[i], pts[i + 1]), P(pts[i + 2], pts[i + 3]));
                if (view.tool is Tool.Polygon or Tool.FilledPolygon)
                {
                    canvas.FillColor = Theme.Accent;
                    for (int i = 0; i + 1 < pts.Count; i += 2) canvas.FillCircle(P(pts[i], pts[i + 1]), 3.5f);
                    if (view.dragEnd is { } cur) canvas.DrawLine(P(pts[^2], pts[^1]), P(cur.X, cur.Y));
                }
            }
        }
    }
}
