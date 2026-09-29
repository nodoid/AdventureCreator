using System.Collections.ObjectModel;
using AdventureCreator.Core.Graphics;
using AdventureCreator.Core.Model;
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace AdventureCreator.Studio.Controls;

/// <summary>
/// Vector picture editor: lines, rectangles, ellipses, polygons, freehand, flood fill, pattern shading, text,
/// attribute blocks, sub-picture calls and bitmap stamps, drawn onto the same software renderer the games use.
/// </summary>
public sealed class PictureEditorView : ContentView
{
    public enum Tool { Line, Rectangle, FilledRectangle, Ellipse, FilledEllipse, Polygon, FilledPolygon, Freehand, Plot, Fill, Shade, Text, Block, Call, Stamp }

    private static readonly (Tool Tool, string Label, string Tip)[] ToolButtons =
    {
        (Tool.Line, "╱ Line", "Drag to draw a line"),
        (Tool.Rectangle, "▭ Rect", "Drag to draw a rectangle"),
        (Tool.FilledRectangle, "▬ Box", "Drag to draw a filled rectangle"),
        (Tool.Ellipse, "◯ Oval", "Drag to draw an ellipse"),
        (Tool.FilledEllipse, "⬤ Disc", "Drag to draw a filled ellipse"),
        (Tool.Polygon, "⬠ Poly", "Click points; click the first point or Finish to close"),
        (Tool.FilledPolygon, "⬟ Shape", "Click points of a filled shape; Finish to close"),
        (Tool.Freehand, "✎ Pen", "Drag to draw freehand"),
        (Tool.Plot, "· Plot", "Click to plot a pixel"),
        (Tool.Fill, "▨ Fill", "Flood fill an area with the ink colour"),
        (Tool.Shade, "▦ Shade", "Flood fill with a pattern"),
        (Tool.Text, "A Text", "Click to place text"),
        (Tool.Block, "▣ Attr", "Drag to set ink/paper of 8×8 cells (Spectrum mode)"),
        (Tool.Call, "⧉ Sub", "Click to place a sub-picture"),
        (Tool.Stamp, "🖼 Image", "Drag to place an imported image"),
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

    private readonly Picture picture;
    private readonly EditorContext ctx;
    private readonly GraphicsView canvas;
    private readonly CanvasDrawable drawable;
    private readonly ObservableCollection<string> commandTexts = new();
    private readonly CollectionView commandList;
    private readonly Dictionary<Tool, Chip> toolButtonMap = new();
    private readonly FlexLayout palette = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly Label status = new() { FontSize = 12, Opacity = 0.75 };
    private readonly Button finishButton;
    private readonly Stepper brush = new() { Minimum = 1, Maximum = 12, Value = 1, Increment = 1 };

    private Tool tool = Tool.Line;
    private int ink;
    private int shadePattern;
    private int? previewLimit;
    private (int X, int Y)? dragStart;
    private (int X, int Y)? dragEnd;
    private readonly List<int> pendingPoints = new();

    public PictureEditorView(Picture picture, EditorContext ctx)
    {
        this.picture = picture;
        this.ctx = ctx;
        ink = picture.InitialInk;

        drawable = new CanvasDrawable(this);
        canvas = new GraphicsView { Drawable = drawable, HeightRequest = 540, MinimumWidthRequest = 520, BackgroundColor = Maui.Theme.Canvas };
        canvas.StartInteraction += (_, e) => OnStart(e.Touches[0]);
        canvas.DragInteraction += (_, e) => OnDrag(e.Touches[0]);
        canvas.EndInteraction += (_, e) => OnEnd(e.Touches.Length > 0 ? e.Touches[0] : null);
        canvas.SizeChanged += (_, _) => RefreshImage();

        // ---- tools
        var tools = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (t, label, tip) in ToolButtons)
        {
            var b = new Chip(label, tip);
            b.Clicked += (_, _) => SelectTool(t);
            toolButtonMap[t] = b;
            tools.Children.Add(b);
        }
        finishButton = new Button { Text = "Finish shape", IsVisible = false, FontSize = 12, HeightRequest = 30 };
        finishButton.Clicked += (_, _) => FinishPolygon();

        var brushLabel = new Label { Text = "Brush 1", VerticalOptions = LayoutOptions.Center, FontSize = 12 };
        brush.ValueChanged += (_, e) => brushLabel.Text = $"Brush {(int)e.NewValue}";

        var patternButton = new Button { Text = "Pattern 1", FontSize = 12, HeightRequest = 30 };
        patternButton.Clicked += (_, _) =>
        {
            shadePattern = (shadePattern + 1) % ShadePatterns.Length;
            patternButton.Text = $"Pattern {shadePattern + 1}";
        };

        var background = new Button { Text = "Set background to ink", FontSize = 12, HeightRequest = 30 };
        ToolTipProperties.SetText(background, "Adds (or replaces) a Clear command at the start of the picture");
        background.Clicked += (_, _) =>
        {
            if (picture.Commands.Count > 0 && picture.Commands[0].Op == DrawOp.Clear) picture.Commands[0].Color = ink;
            else picture.Commands.Insert(0, new DrawCommand { Op = DrawOp.Clear, Color = ink });
            picture.InitialPaper = ink;
            Changed();
        };

        var importImage = new Button { Text = "Import background image…", FontSize = 12, HeightRequest = 30 };
        importImage.Clicked += async (_, _) => await ImportImageAsync(asBackground: true);
        var importStamp = new Button { Text = "Import image to stamp…", FontSize = 12, HeightRequest = 30 };
        importStamp.Clicked += async (_, _) => await ImportImageAsync(asBackground: false);

        BuildPalette();

        // ---- command list
        commandList = new CollectionView
        {
            ItemsSource = commandTexts,
            SelectionMode = SelectionMode.Single,
            HeightRequest = 420,
            ItemTemplate = new DataTemplate(() =>
            {
                var l = new Label { FontSize = 12, Padding = new Thickness(6, 3), FontFamily = "Menlo" };
                l.SetBinding(Label.TextProperty, ".");
                return l;
            }),
        };
        commandList.SelectionChanged += (_, _) =>
        {
            if (previewLimit != null) { previewLimit = SelectedIndex + 1; RefreshImage(); }
        };

        var del = CmdButton("Delete", () => { if (SelectedIndex >= 0) { picture.Commands.RemoveAt(SelectedIndex); Changed(); } });
        var up = CmdButton("↑", () => MoveCommand(-1));
        var down = CmdButton("↓", () => MoveCommand(1));
        var undo = CmdButton("Undo last", () => { if (picture.Commands.Count > 0) { picture.Commands.RemoveAt(picture.Commands.Count - 1); Changed(); } });
        var step = CmdButton("Step view", () =>
        {
            previewLimit = previewLimit == null ? Math.Max(1, SelectedIndex + 1) : null;
            RefreshImage();
        });
        ToolTipProperties.SetText(step, "Show the picture only up to the selected command");
        var clear = CmdButton("Clear all", async () =>
        {
            if (await ctx.PageProvider().DisplayAlertAsync("Clear picture", "Remove all drawing commands?", "Clear", "Cancel"))
            {
                picture.Commands.Clear();
                Changed();
            }
        });

        var properties = new ObjectEditor(picture, ctx, only: new[] { "Id", "Name", "Width", "Height", "RenderMode", "InitialInk", "InitialPaper", "BitmapAsset", "IsSubroutine" },
            onStructureChanged: RefreshImage);

        var top = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                tools,
                PaletteRow(),
                new HorizontalStackLayout { Spacing = 8, Children = { finishButton, brushLabel, brush, patternButton, background } },
            },
        };
        var left = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                canvas,
                status,
                new HorizontalStackLayout { Spacing = 6, Children = { importImage, importStamp } },
            },
        };
        var right = new VerticalStackLayout
        {
            Spacing = 6,
            WidthRequest = 280,
            Children =
            {
                new Label { Text = "Drawing commands", FontAttributes = FontAttributes.Bold },
                new Border { Content = commandList, Stroke = Colors.Gray.WithAlpha(0.3f), Padding = 2 },
                new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { del, up, down, undo, step, clear } },
                new SectionView("Picture", properties),
            },
        };
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 16, RowSpacing = 10 };
        grid.Add(top, 0, 0);
        Grid.SetColumnSpan(top, 2);
        grid.Add(left, 0, 1);
        grid.Add(right, 1, 1);
        Content = grid;

        SelectTool(Tool.Line);
        RefreshList();
    }

    private View PaletteRow()
    {
        var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8 };
        g.Add(new Label { Text = "Ink", FontSize = 12, Opacity = 0.7, VerticalOptions = LayoutOptions.Center }, 0);
        g.Add(palette, 1);
        return g;
    }

    private int SelectedIndex => commandList.SelectedItem is string s ? commandTexts.IndexOf(s) : -1;

    private Button CmdButton(string text, Action action)
    {
        var b = new Button { Text = text, FontSize = 12, HeightRequest = 28, Padding = new Thickness(8, 0), Margin = new Thickness(0, 0, 4, 4) };
        b.Clicked += (_, _) => action();
        return b;
    }

    private void BuildPalette()
    {
        palette.Children.Clear();
        for (int i = 0; i < picture.Palette.Count; i++)
        {
            int index = i;
            var swatch = new Border
            {
                WidthRequest = 20,
                HeightRequest = 20,
                Margin = new Thickness(0, 0, 3, 3),
                BackgroundColor = Maui.PictureImages.FromArgb(picture.Palette[i]),
                StrokeThickness = index == ink ? 3 : 1,
                Stroke = index == ink ? Colors.Orange : Colors.Gray,
            };
            ToolTipProperties.SetText(swatch, $"Colour {index}");
            swatch.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { ink = index; BuildPalette(); }) });
            palette.Children.Add(swatch);
        }
    }

    private void SelectTool(Tool t)
    {
        tool = t;
        pendingPoints.Clear();
        finishButton.IsVisible = false;
        foreach (var (k, b) in toolButtonMap) b.IsSelected = k == t;
        status.Text = ToolButtons.First(x => x.Tool == t).Tip;
        canvas.Invalidate();
    }

    private void MoveCommand(int delta)
    {
        int i = SelectedIndex;
        int to = i + delta;
        if (i < 0 || to < 0 || to >= picture.Commands.Count) return;
        var c = picture.Commands[i];
        picture.Commands.RemoveAt(i);
        picture.Commands.Insert(to, c);
        Changed();
        commandList.SelectedItem = commandTexts[to];
    }

    private void Changed()
    {
        ctx.Changed(picture);
        RefreshList();
        RefreshImage();
    }

    private void RefreshList()
    {
        commandTexts.Clear();
        for (int i = 0; i < picture.Commands.Count; i++) commandTexts.Add($"{i + 1,3}  {picture.Commands[i]}");
    }

    // ------------------------------------------------------------------ rendering

    internal IImage? Image;
    internal int Scale = 1;
    internal float OffsetX, OffsetY;

    private void RefreshImage()
    {
        if (canvas.Width <= 0 || canvas.Height <= 0) return;
        Scale = Math.Max(1, (int)Math.Min(canvas.Width / picture.Width, canvas.Height / picture.Height));
        try
        {
            var png = PictureRenderer.RenderPng(picture, ctx.Adventure, Scale, previewLimit);
            Image = PlatformImage.FromStream(new MemoryStream(png));
        }
        catch (Exception ex)
        {
            status.Text = "Render error: " + ex.Message;
        }
        canvas.Invalidate();
    }

    private (int X, int Y) ToPicture(PointF p) =>
        ((int)Math.Floor((p.X - OffsetX) / Scale), (int)Math.Floor((p.Y - OffsetY) / Scale));

    private void EnsureInk()
    {
        int current = picture.InitialInk;
        foreach (var c in picture.Commands) if (c.Op == DrawOp.SetInk) current = c.Color;
        if (current != ink) picture.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = ink });
    }

    // ------------------------------------------------------------------ interaction

    private void OnStart(PointF p)
    {
        var pt = ToPicture(p);
        dragStart = pt;
        dragEnd = pt;
        if (tool == Tool.Freehand) { pendingPoints.Clear(); pendingPoints.Add(pt.X); pendingPoints.Add(pt.Y); }
        status.Text = $"{pt.X}, {pt.Y}";
        canvas.Invalidate();
    }

    private void OnDrag(PointF p)
    {
        var pt = ToPicture(p);
        dragEnd = pt;
        if (tool == Tool.Freehand && (pendingPoints.Count < 2 || pendingPoints[^2] != pt.X || pendingPoints[^1] != pt.Y))
        {
            pendingPoints.Add(pt.X);
            pendingPoints.Add(pt.Y);
        }
        status.Text = dragStart is { } s ? $"{s.X}, {s.Y} → {pt.X}, {pt.Y}" : $"{pt.X}, {pt.Y}";
        canvas.Invalidate();
    }

    private async void OnEnd(PointF? p)
    {
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
                picture.Commands.Add(new DrawCommand { Op = DrawOp.AttributeBlock, X = a.X, Y = a.Y, X2 = b.X, Y2 = b.Y, Color = ink, Color2 = -1 });
                break;
            case Tool.Plot:
                EnsureInk();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Plot, X = b.X, Y = b.Y });
                break;
            case Tool.Fill:
                EnsureInk();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Fill, X = b.X, Y = b.Y });
                break;
            case Tool.Shade:
                EnsureInk();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Shade, X = b.X, Y = b.Y, Pattern = ShadePatterns[shadePattern].ToArray() });
                break;
            case Tool.Freehand:
                if (pendingPoints.Count >= 2)
                {
                    EnsureInk();
                    picture.Commands.Add(new DrawCommand { Op = DrawOp.Freehand, Points = pendingPoints.ToList(), Scale = (int)brush.Value });
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
                finishButton.IsVisible = pendingPoints.Count >= 6;
                canvas.Invalidate();
                return;
            case Tool.Text:
            {
                var text = await ctx.PageProvider().DisplayPromptAsync("Text", "Text to draw:");
                if (string.IsNullOrEmpty(text)) return;
                EnsureInk();
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Text, X = b.X, Y = b.Y, Text = text, Scale = (int)brush.Value });
                break;
            }
            case Tool.Call:
            {
                var subs = ctx.Adventure.Pictures.Where(x => x != picture).ToList();
                if (subs.Count == 0) { await ctx.PageProvider().DisplayAlertAsync("No pictures", "Create another picture to use as a sub-picture first.", "OK"); return; }
                var choice = await ctx.PageProvider().DisplayActionSheetAsync("Sub-picture", "Cancel", null, subs.Select(s => $"{s.Name} ({s.Id})").ToArray());
                var sub = subs.FirstOrDefault(s => $"{s.Name} ({s.Id})" == choice);
                if (sub == null) return;
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Call, SubPictureId = sub.Id, X = b.X, Y = b.Y, Scale = 8 });
                break;
            }
            case Tool.Stamp:
            {
                var images = ctx.Adventure.Assets.Keys.Where(k => k.StartsWith("images/", StringComparison.OrdinalIgnoreCase)).ToList();
                if (images.Count == 0) { await ctx.PageProvider().DisplayAlertAsync("No images", "Use \"Import image to stamp…\" first.", "OK"); return; }
                var choice = await ctx.PageProvider().DisplayActionSheetAsync("Image", "Cancel", null, images.ToArray());
                if (choice == null || choice == "Cancel") return;
                int w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
                picture.Commands.Add(new DrawCommand { Op = DrawOp.Image, Text = choice, X = Math.Min(a.X, b.X), Y = Math.Min(a.Y, b.Y), X2 = w > 2 ? w : 0, Y2 = h > 2 ? h : 0 });
                break;
            }
        }
        Changed();
    }

    private void FinishPolygon()
    {
        if (pendingPoints.Count >= 4)
        {
            EnsureInk();
            picture.Commands.Add(new DrawCommand { Op = tool == Tool.FilledPolygon ? DrawOp.FilledPolygon : DrawOp.Polygon, Points = pendingPoints.ToList() });
        }
        pendingPoints.Clear();
        finishButton.IsVisible = false;
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

    // ------------------------------------------------------------------ drawable

    private sealed class CanvasDrawable : IDrawable
    {
        private readonly PictureEditorView view;
        public CanvasDrawable(PictureEditorView view) => this.view = view;

        public void Draw(ICanvas canvas, RectF rect)
        {
            var pic = view.picture;
            int s = view.Scale;
            float w = pic.Width * s, h = pic.Height * s;
            view.OffsetX = (float)Math.Floor((rect.Width - w) / 2);
            view.OffsetY = (float)Math.Floor((rect.Height - h) / 2);
            float ox = view.OffsetX, oy = view.OffsetY;

            canvas.Antialias = false;
            if (view.Image != null) canvas.DrawImage(view.Image, ox, oy, w, h);
            canvas.StrokeColor = Colors.Gray;
            canvas.StrokeSize = 1;
            canvas.DrawRectangle(ox - 1, oy - 1, w + 2, h + 2);

            // Previews
            var inkColour = view.ink < pic.Palette.Count ? Maui.PictureImages.FromArgb(pic.Palette[view.ink]) : Colors.White;
            canvas.StrokeColor = inkColour;
            canvas.StrokeSize = Math.Max(1, s);
            canvas.StrokeDashPattern = null;
            PointF P(int x, int y) => new(ox + x * s + s / 2f, oy + y * s + s / 2f);

            if (view.dragStart is { } a && view.dragEnd is { } b)
            {
                switch (view.tool)
                {
                    case Tool.Line:
                        canvas.DrawLine(P(a.X, a.Y), P(b.X, b.Y));
                        break;
                    case Tool.Rectangle:
                    case Tool.FilledRectangle:
                    case Tool.Block:
                    case Tool.Stamp:
                        canvas.DrawRectangle(RectF.FromLTRB(P(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)).X, P(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)).Y,
                            P(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)).X, P(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)).Y));
                        break;
                    case Tool.Ellipse:
                    case Tool.FilledEllipse:
                        canvas.DrawEllipse(RectF.FromLTRB(P(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)).X, P(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)).Y,
                            P(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)).X, P(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)).Y));
                        break;
                }
            }
            var pts = view.pendingPoints;
            if (pts.Count >= 2)
            {
                if (view.tool == Tool.Freehand) canvas.StrokeSize = Math.Max(1, s * (float)view.brush.Value);
                for (int i = 0; i + 3 < pts.Count; i += 2) canvas.DrawLine(P(pts[i], pts[i + 1]), P(pts[i + 2], pts[i + 3]));
                if (view.tool is Tool.Polygon or Tool.FilledPolygon)
                {
                    canvas.FillColor = Colors.Orange;
                    for (int i = 0; i + 1 < pts.Count; i += 2) canvas.FillCircle(P(pts[i], pts[i + 1]), 3);
                    if (view.dragEnd is { } cur) canvas.DrawLine(P(pts[^2], pts[^1]), P(cur.X, cur.Y));
                }
            }
        }
    }
}
