namespace AdventureCreator.Maui;

/// <summary>Sets the initial desktop window size (Mac Catalyst ignores Window.Width/Height, so use the scene API there).</summary>
public static class WindowSizing
{
    /// <summary>Sets the preferred size (clamped to the screen), centres the window, and sets a minimum size.</summary>
    public static void Apply(Window window, double width, double height, double minWidth, double minHeight)
    {
        // Never ask for more than the screen: small displays and VMs would otherwise get a window that runs off-screen.
        var display = DeviceDisplay.Current.MainDisplayInfo;
        double density = display.Density > 0 ? display.Density : 1;
        double screenW = display.Width / density, screenH = display.Height / density;
        if (screenW > 0 && screenH > 0)
        {
            width = Math.Min(width, screenW - 40);
            height = Math.Min(height, screenH - 80);
            minWidth = Math.Min(minWidth, width);
            minHeight = Math.Min(minHeight, height);
        }
        window.MinimumWidth = minWidth;
        window.MinimumHeight = minHeight;
#if MACCATALYST
        bool done = false;
        window.Activated += (_, _) =>
        {
            if (done || window.Handler?.PlatformView is not UIKit.UIWindow native || native.WindowScene is not { } scene) return;
            done = true;
            var screen = scene.Screen.Bounds;
            double w = Math.Min(width, screen.Width - 40), h = Math.Min(height, screen.Height - 60);
            var frame = new CoreGraphics.CGRect((screen.Width - w) / 2, (screen.Height - h) / 2, w, h);
            scene.RequestGeometryUpdate(new UIKit.UIWindowSceneGeometryPreferencesMac(frame), _ => { });
            // Some macOS versions ignore the geometry request for the first window; forcing the minimum size
            // briefly makes the window grow, after which the normal minimum is restored.
            if (scene.SizeRestrictions is { } restrictions)
            {
                var previous = restrictions.MinimumSize;
                restrictions.MinimumSize = new CoreGraphics.CGSize(w, h);
                window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () => restrictions.MinimumSize = previous);
            }
        };
#else
        window.Width = width;
        window.Height = height;
        if (screenW > 0 && screenH > 0)
        {
            window.X = Math.Max(0, (screenW - width) / 2);
            window.Y = Math.Max(0, (screenH - height) / 2 - 20);
        }
#endif
    }
}
