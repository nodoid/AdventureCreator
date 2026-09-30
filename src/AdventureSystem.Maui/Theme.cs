namespace AdventureSystem.Maui;

/// <summary>The light colour scheme shared by the Studio and the Player.</summary>
public static class Theme
{
    public static readonly Color Window = Color.FromArgb("#F5F5F7");
    public static readonly Color Sidebar = Color.FromArgb("#E8E9ED");
    public static readonly Color Pane = Color.FromArgb("#FFFFFF");
    public static readonly Color Hover = Color.FromArgb("#DDE1E8");
    public static readonly Color Selected = Color.FromArgb("#D3E2FB");
    public static readonly Color Accent = Color.FromArgb("#A0661B");
    public static readonly Color Text = Color.FromArgb("#1D1D1F");
    public static readonly Color SecondaryText = Color.FromArgb("#6E6E73");
    public static readonly Color Border = Color.FromArgb("#C7C7CC");
    public static readonly Color Chip = Color.FromArgb("#D2DAE7");
    public static readonly Color ChipHover = Color.FromArgb("#BFCADB");
    public static readonly Color ChipBorder = Color.FromArgb("#8F9BB0");
    public static readonly Color Canvas = Color.FromArgb("#D8DAE0");
    public static readonly Color MapRoom = Color.FromArgb("#DCE6F7");
    public static readonly Color MapDarkRoom = Color.FromArgb("#B8BCC6");
    public static readonly Color MapLink = Color.FromArgb("#8E8E93");
    public static readonly Color MapOneWay = Color.FromArgb("#2F7FD8");
    public static readonly Color MapDoor = Color.FromArgb("#D9822B");

    /// <summary>Default game page colours (games can override them in their settings).</summary>
    public const string GameBackground = "#FBFAF6";
    public const string GameText = "#1F1F1F";

    /// <summary>A slightly darker (on light backgrounds) or lighter (on dark ones) shade for bars and fields.</summary>
    public static Color Shade(Color background, float amount) =>
        background.GetLuminosity() > 0.5f ? background.AddLuminosity(-amount) : background.AddLuminosity(amount);
}
