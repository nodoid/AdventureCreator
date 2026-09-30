using AdventureSystem.Core.Model;

namespace AdventureSystem.Studio.Controls;

/// <summary>
/// Automatic map of the rooms: lays rooms out on a grid following compass exits from the start room and draws
/// the connections. Clicking a room selects it for editing. On the iPad a room opens on a tap (so a pan that starts on
/// a room still scrolls), and pinching zooms.
/// </summary>
public sealed class MapView : ContentView
{
    private const float CellW = 170, CellH = 90, BoxW = 130, BoxH = 48;
    private const double MinZoom = 0.5, MaxZoom = 2.5;
    private readonly Adventure adventure;
    private readonly GraphicsView view;
    private readonly ScrollView scroll;
    private readonly Dictionary<string, (int X, int Y)> positions = new(StringComparer.OrdinalIgnoreCase);
    private int minX, minY;
    private readonly int mapW, mapH;
    /// <summary>Pinch zoom (iPad only; always 1 on the desktop).</summary>
    private double zoom = 1;
    private bool pinching;

    public event Action<Room>? RoomClicked;

    public MapView(Adventure adventure)
    {
        this.adventure = adventure;
        Layout();
        view = new GraphicsView { Drawable = new MapDrawable(this) };
        mapW = positions.Count == 0 ? 400 : (positions.Values.Max(p => p.X) - minX + 1) * (int)CellW + 60;
        mapH = positions.Count == 0 ? 300 : (positions.Values.Max(p => p.Y) - minY + 1) * (int)CellH + 60;
        SizeView();
        if (TouchMetrics.IsTouch) AddTouch();
        else
            view.StartInteraction += (_, e) =>
            {
                var p = e.Touches[0];
                foreach (var (id, pos) in positions)
                {
                    var r = BoxRect(pos);
                    if (r.Contains(p) && adventure.FindRoom(id) is { } room) RoomClicked?.Invoke(room);
                }
            };
        Content = scroll = new ScrollView { Orientation = ScrollOrientation.Both, Content = view };
    }

    private void SizeView()
    {
        view.WidthRequest = Math.Max(600, mapW * zoom);
        view.HeightRequest = Math.Max(400, mapH * zoom);
    }

    /// <summary>
    /// A room opens only on a tap: one finger, lifted close to where it went down. Anything else (a pan, which the
    /// scroll view takes over, or a pinch) is left alone.
    /// </summary>
    private void AddTouch()
    {
        const float slop = 10;
        PointF down = default;
        bool tap = false;
        view.StartInteraction += (_, e) => { down = e.Touches[0]; tap = e.Touches.Length == 1 && !pinching; };
        view.DragInteraction += (_, e) => { if (e.Touches.Length != 1 || Distance(e.Touches[0], down) > slop) tap = false; };
        view.CancelInteraction += (_, _) => tap = false;
        view.EndInteraction += (_, e) =>
        {
            var p = e.Touches.Length > 0 ? e.Touches[0] : down;
            if (!tap || pinching || Distance(p, down) > slop) return;
            tap = false;
            if (RoomAt(p) is { } room) RoomClicked?.Invoke(room);
        };

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, e) =>
        {
            if (e.Status == GestureStatus.Started) { pinching = true; tap = false; }
            else if (e.Status != GestureStatus.Running) { pinching = false; return; }
            double old = zoom;
            zoom = Math.Clamp(zoom * e.Scale, MinZoom, MaxZoom);
            if (Math.Abs(zoom - old) < 1e-6) return;
            // Keep the point between the fingers where it is.
            double ox = e.ScaleOrigin.X * view.Width, oy = e.ScaleOrigin.Y * view.Height, f = zoom / old - 1;
            SizeView();
            view.Invalidate();
            _ = scroll.ScrollToAsync(Math.Max(0, scroll.ScrollX + ox * f), Math.Max(0, scroll.ScrollY + oy * f), false);
        };
        view.GestureRecognizers.Add(pinch);
    }

    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The room under a finger at <paramref name="p"/> (view points): the nearest box within 12 points, so small boxes when zoomed out are still easy to hit.</summary>
    private Room? RoomAt(PointF p)
    {
        float z = (float)zoom, x = p.X / z, y = p.Y / z, reach = 12 / z, best = float.MaxValue;
        Room? found = null;
        foreach (var (id, pos) in positions)
        {
            var r = BoxRect(pos);
            float dx = MathF.Max(0, MathF.Max(r.Left - x, x - r.Right)), dy = MathF.Max(0, MathF.Max(r.Top - y, y - r.Bottom));
            if (dx > reach || dy > reach || dx * dx + dy * dy >= best || adventure.FindRoom(id) is not { } room) continue;
            best = dx * dx + dy * dy;
            found = room;
        }
        return found;
    }

    private static readonly Dictionary<string, (int dx, int dy)> Offsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["north"] = (0, -1), ["south"] = (0, 1), ["east"] = (1, 0), ["west"] = (-1, 0),
        ["northeast"] = (1, -1), ["northwest"] = (-1, -1), ["southeast"] = (1, 1), ["southwest"] = (-1, 1),
        ["up"] = (1, -1), ["down"] = (-1, 1), ["in"] = (1, 1), ["out"] = (-1, -1),
    };

    private void Layout()
    {
        var occupied = new HashSet<(int, int)>();
        var queue = new Queue<string>();
        void Place(string id, (int X, int Y) at)
        {
            // Nudge to a free cell if needed.
            var p = at;
            int ring = 0;
            while (occupied.Contains(p))
            {
                ring++;
                p = (at.X + ring % 3 - 1 + (ring / 3), at.Y + ring / 2);
            }
            positions[id] = p;
            occupied.Add(p);
            queue.Enqueue(id);
        }

        var start = adventure.FindRoom(adventure.StartRoomId) ?? adventure.Rooms.FirstOrDefault();
        if (start == null) return;
        Place(start.Id, (0, 0));
        int nextIsland = 0;
        while (true)
        {
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                var room = adventure.FindRoom(id);
                if (room == null) continue;
                foreach (var exit in room.Exits)
                {
                    if (positions.ContainsKey(exit.TargetRoomId) || adventure.FindRoom(exit.TargetRoomId) == null) continue;
                    var (dx, dy) = Offsets.TryGetValue(exit.Direction, out var o) ? o : (1, 1);
                    var from = positions[id];
                    Place(exit.TargetRoomId, (from.X + dx, from.Y + dy));
                }
            }
            // Rooms not reachable by exits: place them in a row below.
            var unplaced = adventure.Rooms.FirstOrDefault(r => !positions.ContainsKey(r.Id));
            if (unplaced == null) break;
            int maxY = positions.Values.Max(p => p.Y);
            Place(unplaced.Id, (nextIsland++ * 1, maxY + 2));
        }
        minX = positions.Values.Min(p => p.X);
        minY = positions.Values.Min(p => p.Y);
    }

    private RectF BoxRect((int X, int Y) p) =>
        new(30 + (p.X - minX) * CellW + (CellW - BoxW) / 2, 30 + (p.Y - minY) * CellH + (CellH - BoxH) / 2, BoxW, BoxH);

    private sealed class MapDrawable : IDrawable
    {
        private readonly MapView map;
        public MapDrawable(MapView map) => this.map = map;

        public void Draw(ICanvas canvas, RectF dirty)
        {
            canvas.Antialias = true;
            if (map.zoom != 1) canvas.Scale((float)map.zoom, (float)map.zoom);
            canvas.StrokeSize = 2;
            // connections
            foreach (var room in map.adventure.Rooms)
            {
                if (!map.positions.TryGetValue(room.Id, out var a)) continue;
                foreach (var exit in room.Exits)
                {
                    if (!map.positions.TryGetValue(exit.TargetRoomId, out var b)) continue;
                    var ra = map.BoxRect(a);
                    var rb = map.BoxRect(b);
                    bool twoWay = map.adventure.FindRoom(exit.TargetRoomId)?.Exits.Any(e => string.Equals(e.TargetRoomId, room.Id, StringComparison.OrdinalIgnoreCase)) == true;
                    canvas.StrokeColor = exit.DoorItemId != null ? Maui.Theme.MapDoor : twoWay ? Maui.Theme.MapLink : Maui.Theme.MapOneWay;
                    canvas.StrokeDashPattern = exit.Hidden ? new float[] { 4, 4 } : null;
                    canvas.DrawLine(ra.Center, rb.Center);
                    if (!twoWay)
                    {
                        // arrow head for one-way exits
                        var dir = new Vector2(rb.Center.X - ra.Center.X, rb.Center.Y - ra.Center.Y);
                        var len = MathF.Max(1, MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
                        var tip = new PointF(rb.Center.X - dir.X / len * 30, rb.Center.Y - dir.Y / len * 30);
                        canvas.FillColor = Maui.Theme.MapOneWay;
                        canvas.FillCircle(tip, 4);
                    }
                }
            }
            canvas.StrokeDashPattern = null;
            // rooms
            foreach (var (id, pos) in map.positions)
            {
                var room = map.adventure.FindRoom(id);
                if (room == null) continue;
                var r = map.BoxRect(pos);
                bool start = string.Equals(id, map.adventure.StartRoomId, StringComparison.OrdinalIgnoreCase);
                canvas.FillColor = room.IsDark ? Maui.Theme.MapDarkRoom : Maui.Theme.MapRoom;
                canvas.FillRoundedRectangle(r, 8);
                canvas.StrokeColor = start ? Maui.Theme.Accent : Maui.Theme.Border;
                canvas.StrokeSize = start ? 3 : 1.5f;
                canvas.DrawRoundedRectangle(r, 8);
                canvas.FontColor = Maui.Theme.Text;
                canvas.FontSize = 12;
                canvas.DrawString(string.IsNullOrWhiteSpace(room.Name) ? room.Id : room.Name, r.Inflate(-4, -4), HorizontalAlignment.Center, VerticalAlignment.Center);
                int items = map.adventure.Items.Count(i => string.Equals(i.Location, id, StringComparison.OrdinalIgnoreCase));
                if (items > 0 || room.PictureId != null || room.SoundId != null)
                {
                    canvas.FontSize = 10;
                    canvas.FontColor = Maui.Theme.SecondaryText;
                    var badges = (items > 0 ? $"{items} item{(items == 1 ? "" : "s")} " : "") + (room.PictureId != null ? "🖼 " : "") + (room.SoundId != null ? "♪" : "");
                    canvas.DrawString(badges, new RectF(r.X, r.Bottom - 14, r.Width, 12), HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }
        }
    }

    private readonly record struct Vector2(float X, float Y);
}
