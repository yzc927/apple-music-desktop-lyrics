namespace AppleMusicDesktopLyrics;

internal static class OverlayLayout
{
    public static double Scale(double width, double height, double currentWidth,
        double nextWidth, double textHeight)
    {
        var preferred = Math.Clamp(Math.Sqrt(width / 920 * height / 150), 0.58, 2.5);
        var availableWidth = Math.Max(1, width - 64);
        var availableHeight = Math.Max(1, height - 54);
        return Math.Max(0.01, Math.Min(preferred,
            Math.Min(availableWidth / Math.Max(1, Math.Max(currentWidth, nextWidth)),
                availableHeight / Math.Max(1, textHeight))));
    }

    public static int ResizeHit(double x, double y, double width, double height)
    {
        if (x < 0 || y < 0 || x > width || y > height) return 0;
        var left = x < 8;
        var right = x >= width - 8;
        var top = y < 8;
        var bottom = y >= height - 8;
        if (top) return left ? 13 : right ? 14 : 12;
        if (bottom) return left ? 16 : right ? 17 : 15;
        return left ? 10 : right ? 11 : 0;
    }
}
