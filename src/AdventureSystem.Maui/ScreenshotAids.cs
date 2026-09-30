namespace AdventureSystem.Maui;

/// <summary>Development aids for taking store screenshots from the command line (Debug builds only).</summary>
public static class ScreenshotAids
{
    /// <summary>AC_ORIENTATION=landscape|portrait turns an iPad or iPhone app that way once its window is up.</summary>
    public static void Apply(Window window)
    {
#if DEBUG && IOS
        var want = Environment.GetEnvironmentVariable("AC_ORIENTATION");
        if (string.IsNullOrEmpty(want)) return;
        var mask = want.StartsWith("land", StringComparison.OrdinalIgnoreCase) ? UIKit.UIInterfaceOrientationMask.LandscapeRight : UIKit.UIInterfaceOrientationMask.Portrait;
        bool done = false;
        window.Activated += (_, _) =>
        {
            if (done) return;
            done = true;
            // Once the scene is up; the view controller has to be told its supported orientations may have changed.
            window.Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1), () =>
            {
                var scene = UIKit.UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIKit.UIWindowScene>().FirstOrDefault();
                if (scene == null) { Console.WriteLine("AC_ORIENTATION: no window scene"); return; }
                scene.KeyWindow?.RootViewController?.SetNeedsUpdateOfSupportedInterfaceOrientations();
                scene.RequestGeometryUpdate(new UIKit.UIWindowSceneGeometryPreferencesIOS(mask), e => Console.WriteLine("AC_ORIENTATION: " + e.LocalizedDescription));
            });
        };
#endif
    }
}
