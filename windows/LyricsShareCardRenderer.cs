using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace AppleMusicDesktopLyrics;

internal static class LyricsShareCardRenderer
{
    private const int Width = 1200;
    private const int Height = 630;

    public static BitmapSource Render(string title, string artist, string album, string lyric,
        byte[]? artwork, string? backgroundPath, IReadOnlyList<string> palette)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, Width, Height);
            var background = TryLoadFile(backgroundPath);
            if (background is not null)
            {
                DrawCover(drawing, background, bounds);
                drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(172, 0, 0, 0)), null, bounds);
            }
            else
            {
                var colors = palette.Select(ParseColor).ToArray();
                var gradient = new LinearGradientBrush(
                    colors.FirstOrDefault(Color.FromRgb(35, 39, 50)),
                    colors.LastOrDefault(Color.FromRgb(11, 12, 17)), 28);
                drawing.DrawRectangle(gradient, null, bounds);
                drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(105, 0, 0, 0)), null, bounds);
            }

            var cover = TryLoadBytes(artwork);
            if (cover is not null)
            {
                drawing.PushClip(new RectangleGeometry(new Rect(62, 70, 430, 430), 24, 24));
                DrawCover(drawing, cover, new Rect(62, 70, 430, 430));
                drawing.Pop();
            }
            else
            {
                drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                    null, new Rect(62, 70, 430, 430), 24, 24);
                DrawText(drawing, "♫", 132, FontWeights.Normal, Brushes.White,
                    new Point(205, 172), 170, 190);
            }

            DrawText(drawing, string.IsNullOrWhiteSpace(lyric) ? title : lyric, 46,
                FontWeights.SemiBold, Brushes.White, new Point(548, 105), 585, 230);
            DrawText(drawing, title, 31, FontWeights.SemiBold,
                new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                new Point(550, 370), 575, 52);
            DrawText(drawing, string.Join(" · ", new[] { artist, album }
                    .Where(value => !string.IsNullOrWhiteSpace(value))), 23, FontWeights.Normal,
                new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                new Point(550, 430), 575, 70);
            DrawText(drawing, "Apple Music 桌面歌词", 17, FontWeights.Normal,
                new SolidColorBrush(Color.FromArgb(145, 255, 255, 255)),
                new Point(62, 566), 500, 30);
        }

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawText(DrawingContext drawing, string text, double size,
        FontWeight weight, Brush brush, Point origin, double maxWidth, double maxHeight)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"),
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, brush, 1)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = maxHeight,
            Trimming = TextTrimming.CharacterEllipsis
        };
        drawing.DrawText(formatted, origin);
    }

    private static void DrawCover(DrawingContext drawing, ImageSource image, Rect target)
    {
        var sourceRatio = image.Width / Math.Max(1, image.Height);
        var targetRatio = target.Width / target.Height;
        var width = sourceRatio > targetRatio ? target.Height * sourceRatio : target.Width;
        var height = sourceRatio > targetRatio ? target.Height : target.Width / sourceRatio;
        drawing.DrawImage(image, new Rect(
            target.X - (width - target.Width) / 2,
            target.Y - (height - target.Height) / 2, width, height));
    }

    private static BitmapImage? TryLoadBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            return Load(stream);
        }
        catch { return null; }
    }

    private static BitmapImage? TryLoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Load(stream);
        }
        catch { return null; }
    }

    private static BitmapImage Load(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static Color ParseColor(string value)
    {
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!; }
        catch { return Color.FromRgb(35, 39, 50); }
    }
}
