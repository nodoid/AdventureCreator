namespace AdventureCreator.Maui;

/// <summary>Platform keyboard hooks for desktop-style editing.</summary>
public static class KeyboardSupport
{
    /// <summary>Up/Down arrows in the command box walk the command history (Windows). On Apple platforms the
    /// same actions are available from the menu bar (⌘↑ / ⌘↓).</summary>
    public static void AttachHistory(Entry entry, Func<string?> previous, Func<string?> next)
    {
#if WINDOWS
        if (entry.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox box)
        {
            box.KeyDown += (_, e) =>
            {
                string? text = e.Key switch
                {
                    Windows.System.VirtualKey.Up => previous(),
                    Windows.System.VirtualKey.Down => next(),
                    _ => null,
                };
                if (text == null) return;
                entry.Text = text;
                entry.CursorPosition = text.Length;
                e.Handled = true;
            };
        }
#endif
    }
}
