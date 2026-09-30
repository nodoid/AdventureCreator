namespace AdventureSystem.Studio.Controls;

/// <summary>Sizes for a touch screen (the iPad) and for a mouse (Mac, Windows).</summary>
public static class TouchMetrics
{
    /// <summary>Running on the iPad: fingers rather than a pointer, and no hover.</summary>
    public static bool IsTouch { get; } = DeviceInfo.Platform == DevicePlatform.iOS;

    /// <summary>The smallest comfortable tap target: 44 points on the iPad (Apple's guideline).</summary>
    public static double MinTarget => IsTouch ? 44 : 28;

    /// <summary>The mouse size on the desktop, the touch size on the iPad.</summary>
    public static double Pick(double mouse, double touch) => IsTouch ? touch : mouse;
}
