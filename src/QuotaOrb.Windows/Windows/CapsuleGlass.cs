using System.Globalization;
using System.Windows;
using System.Windows.Media;
using QuotaOrb.Core.Domain;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace QuotaOrb.Windows.Windows;

internal sealed class CapsuleGlass : FrameworkElement
{
    internal const double CompactWidth = 74;
    internal const double CompactHeight = 78;
    internal const double ExpandedWidth = 410;
    internal const double ExpandedHeight = 210;
    internal const double Diameter = 62;
    private static readonly Color Ink = Rgb(13, 38, 74);
    private static readonly Color SecondaryInk = Rgb(107, 130, 166);
    private static readonly Color White = Colors.White;
    private static readonly Typeface Type = new("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
    private static readonly (double X, double Y, double Size)[] Bubbles =
        [(0.24, .26, 2.6), (.42, .42, 2), (.62, .24, 3), (.76, .52, 2.2), (.31, .61, 1.8), (.56, .70, 2.4)];

    internal bool Claude { get; init; }
    internal QuotaSnapshot? Snapshot { get; set; }
    internal string? Error { get; set; }
    internal bool Weekly { get; set; }
    internal bool Badge { get; set; }
    internal bool Dark { get; set; }
    internal double Transparency { get; set; } = .55;
    internal bool OpensRight { get; set; } = true;
    internal double OrbTop { get; set; } = 8;
    internal double Expansion { get; set; }
    internal double Phase { get; set; }
    internal bool IsExpanded { get; set; }

    internal Rect OrbRect => new(OpensRight || !IsExpanded ? 6 : 342, OrbTop, Diameter, Diameter);
    internal QuotaWindow? Selected => Weekly ? Snapshot?.Weekly ?? Snapshot?.Current : Snapshot?.Current ?? Snapshot?.Weekly;
    internal string SourceName => Claude ? "Claude Code" : "Codex";
    internal string QuotaText => Selected is { } window
        ? (Error is null ? "" : "~") + Percent(window.RemainingPercent) + "%"
        : Error is null ? "…" : "!";

    private Rect CardRect => new(OpensRight ? 92 : 8, 12, 310, 186);

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var orb = OrbRect;
        var card = CardRect;
        var detailWindow = Snapshot?.Weekly ?? Snapshot?.Current;
        var detailAccent = PaletteFor(Risk(detailWindow, Error));
        var reaction = Math.Min(1, Expansion / .22);
        var neckProgress = 1 - Math.Pow(1 - Math.Clamp((Expansion - .16) / .36, 0, 1), 3);
        var cardProgress = Smooth(Math.Clamp((Expansion - .42) / .58, 0, 1));
        if (neckProgress > .001 || cardProgress > .001)
        {
            var silhouette = cardProgress < .04
                ? MakeNeck(orb, neckProgress, cardProgress)
                : MakeCard(orb, card, cardProgress);
            if (!OpensRight)
            {
                silhouette.Transform = new MatrixTransform(-1, 0, 0, 1, ExpandedWidth, 0);
            }

            DrawGlass(drawing, silhouette, detailAccent, cardProgress);
        }

        var contentOpacity = Math.Clamp(((Expansion - .42) / .58 - .48) / .52, 0, 1);
        if (contentOpacity > 0)
        {
            drawing.PushOpacity(contentOpacity);
            DrawCard(drawing, card, detailWindow, detailAccent);
            drawing.Pop();
        }

        var activeWindow = IsExpanded ? detailWindow : Selected;
        if (!IsExpanded)
        {
            orb.Offset(0, Math.Sin(Phase * .72) * .65);
        }

        var accent = PaletteFor(Risk(activeWindow, Error));
        DrawOrb(drawing, orb, activeWindow, accent, reaction);
        if (Badge && !IsExpanded)
        {
            var center = new Point(orb.X + 31, orb.Bottom - 5);
            drawing.DrawEllipse(Gradient(Alpha(White, .78), Alpha(accent.Soft, .46)),
                Pen(Alpha(White, .90), .85), center, 9, 9);
            drawing.DrawEllipse(null, Pen(Alpha(accent.Secondary, .40), .55), center, 7.6, 7.6);
            DrawSourceMark(drawing, center, 1, accent.Secondary);
        }
    }

    private void DrawOrb(DrawingContext drawing, Rect orb, QuotaWindow? window, Palette accent, double reaction)
    {
        var center = new Point(orb.X + 31, orb.Y + 31);
        var edge = Claude ? accent.Secondary : Rgb(31, 161, 245);
        var glow = new RadialGradientBrush(Alpha(Claude ? accent.Primary : Rgb(31, 184, 255), .18), Colors.Transparent);
        drawing.DrawEllipse(glow, null, new Point(center.X, orb.Bottom + 2), 24, 6);

        var lens = new EllipseGeometry(center, 31, 31);
        var lensTint = Claude ? accent.Soft : Rgb(140, 209, 255);
        drawing.DrawGeometry(new RadialGradientBrush(Alpha(White, .52), Alpha(lensTint, .22))
        {
            GradientOrigin = new Point(.38, .36)
        }, null, lens);
        drawing.PushClip(lens);
        drawing.DrawEllipse(Brush(Alpha(lensTint, .13)), null, center, 24.8, 24.8);

        var fraction = window is { } quota ? Math.Clamp(quota.RemainingPercent / 100, .08, 1) : Error is null ? .55 : .72;
        var waterY = orb.Bottom - 6.2 - 49.6 * fraction;
        var liquid = new StreamGeometry();
        var wave = new StreamGeometry();
        var crest = new StreamGeometry();
        using (var fill = liquid.Open())
        using (var line = wave.Open())
        using (var highlight = crest.Open())
        {
            var left = orb.Left - 4;
            var right = orb.Right + 4;
            fill.BeginFigure(new Point(left, waterY), true, true);
            line.BeginFigure(new Point(left, waterY), false, false);
            highlight.BeginFigure(new Point(left, waterY - 1.4), false, false);
            for (var index = 0; index <= 20; index++)
            {
                var ratio = index / 20d;
                var x = left + (right - left) * ratio;
                var y = waterY - Math.Sin(index * .68 + Phase * (2.2 + reaction * 1.8)) * (1.05 + reaction * 2.2)
                    - (ratio - .5) * Math.Sin(Phase * 1.45) * reaction * 5.5;
                fill.LineTo(new Point(x, y), true, false);
                line.LineTo(new Point(x, y), true, false);
                highlight.LineTo(new Point(x, y - 1.4), true, false);
            }
            fill.LineTo(new Point(right, orb.Bottom + 1), false, false);
            fill.LineTo(new Point(left, orb.Bottom + 1), false, false);
        }

        var liquidColor = Claude ? accent.Primary : Risk(window, Error) switch
        {
            QuotaRisk.Warning => Rgb(245, 201, 138),
            QuotaRisk.Critical => Rgb(245, 145, 133),
            QuotaRisk.Error => Rgb(242, 122, 135),
            _ => Rgb(69, 184, 250)
        };
        drawing.DrawGeometry(Gradient(Alpha(Claude ? accent.Soft : Rgb(171, 235, 255), .28),
            Alpha(liquidColor, .57), vertical: true), null, liquid);
        drawing.DrawGeometry(null, Pen(Alpha(edge, .75), 1), wave);
        drawing.DrawGeometry(null, Pen(Alpha(White, .72), .7), crest);

        foreach (var (x, y, size) in Bubbles)
        {
            var position = (y + (Math.Sin(Phase + x * 8) + 1) * .025) % .88;
            if (position < fraction - .06)
            {
                var bubble = new Point(orb.X + 6.2 + x * 49.6, orb.Bottom - 6.2 - position * 49.6);
                drawing.DrawEllipse(Brush(Alpha(White, .52)), Pen(Alpha(edge, .36), .6), bubble, size / 2, size / 2);
            }
        }

        if (reaction > .01)
        {
            var sweep = (Math.Sin(Phase * 1.4) + 1) * .5;
            drawing.DrawEllipse(new RadialGradientBrush(Alpha(White, .22 * reaction), Colors.Transparent), null,
                new Point(orb.Left - 2 + sweep * 66, center.Y), 6, 30);
        }

        drawing.Pop();
        drawing.DrawGeometry(null, Pen(Alpha(White, .92), 1.25), lens);
        drawing.DrawEllipse(null, Pen(Alpha(Claude ? accent.Secondary : Rgb(92, 163, 219), .40), .65), center, 28.2, 28.2);
        drawing.DrawEllipse(null, Pen(Alpha(White, .70), .60), center, 25.3, 25.3);

        var majorPen = Pen(Alpha(Claude ? accent.Primary : Rgb(20, 161, 255), .82), 1);
        var brightPen = Pen(Alpha(Claude ? accent.Primary : Rgb(20, 161, 255), .82), .45);
        var subtlePen = Pen(Alpha(Claude ? accent.Secondary : Rgb(71, 138, 194), .38), .45);
        var majorSubtlePen = Pen(Alpha(Claude ? accent.Secondary : Rgb(71, 138, 194), .38), 1);
        for (var index = 0; index < 52; index++)
        {
            var angle = index * Math.Tau / 52;
            var major = index % 13 == 0;
            var inner = 22.8 - (major ? 2.8 : 1.5);
            var emphasized = index is > 7 and < 19;
            drawing.DrawLine(emphasized ? (major ? majorPen : brightPen) : (major ? majorSubtlePen : subtlePen),
                Polar(center, angle, inner), Polar(center, angle, 22.8));
        }

        var specular = new StreamGeometry();
        using (var arc = specular.Open())
        {
            arc.BeginFigure(Polar(center, 108 * Math.PI / 180, 29), false, false);
            arc.ArcTo(Polar(center, 212 * Math.PI / 180, 29), new System.Windows.Size(29, 29), 0, false,
                SweepDirection.Counterclockwise, true, false);
        }
        drawing.DrawGeometry(null, Pen(Alpha(White, .72), 1), specular);
        var value = window is null ? Error is null ? "…" : "!" :
            (Error is null ? "" : "~") + Percent(window.RemainingPercent) + "%";
        Write(drawing, value, 15, Ink, new Rect(orb.X + 5, center.Y - 9, 52, 20), TextAlignment.Center);
    }

    private void DrawGlass(DrawingContext drawing, Geometry silhouette, Palette palette, double progress)
    {
        var density = 1 - Transparency;
        var start = Dark ? (Claude ? Rgb(26, 13, 51) : Rgb(10, 26, 48)) : White;
        var end = Dark ? (Claude ? Rgb(64, 26, 102) : Rgb(20, 61, 97))
            : Claude ? Rgb(227, 209, 255) : Rgb(199, 230, 255);
        var opacity = progress < .04 ? .12 + Expansion * .5 : Dark ? .54 + density * .48 : .53 + density * .48;
        var fill = new LinearGradientBrush(Alpha(start, opacity), Alpha(end, opacity * .88),
            new Point(.10, .05), new Point(.9, 1));
        if (progress >= .04)
        {
            drawing.PushTransform(new TranslateTransform(0, 1.4));
            drawing.DrawGeometry(null, Pen(Alpha(Ink, .13), 2.8), silhouette);
            drawing.Pop();
        }
        drawing.DrawGeometry(fill, Pen(Alpha(White, .79), 1), silhouette);
        drawing.PushTransform(new TranslateTransform(-.55, 0));
        drawing.DrawGeometry(null, Pen(Alpha(Claude ? palette.Primary : Rgb(56, 194, 255), .24), .65), silhouette);
        drawing.Pop();
        drawing.PushTransform(new TranslateTransform(.55, 0));
        drawing.DrawGeometry(null, Pen(Alpha(Claude ? palette.Secondary : Rgb(255, 97, 133), .14), .55), silhouette);
        drawing.Pop();
    }

    private void DrawCard(DrawingContext drawing, Rect card, QuotaWindow? window, Palette palette)
    {
        var x = card.X + (OpensRight ? 30 : 24);
        var y = card.Y;
        var main = Dark ? Alpha(White, .96) : Ink;
        var secondary = Dark ? Rgb(195, 211, 237) : Claude ? Rgb(122, 102, 161) : SecondaryInk;
        Write(drawing, "Balance Capsule", 17, main, new Rect(x, y + 11, 185, 25));

        var dot = new Point(card.Right - 22, y + 22);
        var dotColor = Claude ? palette.Primary : Risk(window, Error) switch
        {
            QuotaRisk.Safe => Rgb(255, 153, 82),
            QuotaRisk.Warning => Rgb(245, 201, 138),
            QuotaRisk.Critical => Rgb(245, 145, 133),
            QuotaRisk.Error => Rgb(242, 122, 135),
            _ => Rgb(140, 199, 242)
        };
        drawing.DrawEllipse(Brush(dotColor), Pen(Alpha(White, .76), .7), dot, 4, 4);

        var pillWidth = Claude ? 105 : 78;
        drawing.DrawRoundedRectangle(Brush(Dark ? Alpha(Rgb(56, 94, 140), .60) : Alpha(White, .50)),
            Pen(Alpha(White, Dark ? .54 : .82), .75), new Rect(x, y + 39, pillWidth, 22), 11, 11);
        DrawSourceMark(drawing, new Point(x + 14, y + 50), 1.25, Claude ? palette.Primary : Rgb(51, 145, 250));
        Write(drawing, SourceName, 10.5, Dark ? White : Rgb(59, 82, 117), new Rect(x + 24, y + 43, pillWidth - 28, 16));

        if (window is { } quota)
        {
            var number = Percent(quota.RemainingPercent);
            var numberText = MakeText(number, 34, main);
            drawing.DrawText(numberText, new Point(x, y + 69));
            Write(drawing, "%", 19, main, new Rect(x + numberText.Width + 3, y + 76, 24, 26));
            drawing.DrawRectangle(Brush(Alpha(White, Dark ? .28 : .82)), null, new Rect(x, y + 116, 256, .65));

            if (Snapshot?.TokenUsage is { } usage && !Claude)
            {
                var counts = new[] { usage.TodayTokens, usage.MonthTokens, usage.TotalTokens };
                var labels = new[] { "今日", "本月", "总计" };
                for (var index = 0; index < 3; index++)
                {
                    var left = x + 96 + index * 54;
                    Write(drawing, CompactTokens(counts[index]), 10.5, main, new Rect(left, y + 78, 52, 14), TextAlignment.Center);
                    Write(drawing, labels[index], 8.2, secondary, new Rect(left, y + 94, 52, 13), TextAlignment.Center);
                    if (index != 2)
                    {
                        drawing.DrawRectangle(Brush(Alpha(White, .65)), null, new Rect(left + 53, y + 79, .7, 24));
                    }
                }
            }

            var rowY = y + 127;
            if (Snapshot?.Current is { } five)
            {
                DrawRow(drawing, "5h", five, x, rowY, main);
                rowY += 21;
            }
            if (Snapshot?.Weekly is { } week)
            {
                DrawRow(drawing, "Week", week, x, rowY, main);
            }
        }
        else
        {
            Write(drawing, Error ?? "正在读取额度…", 11.5, Error is null ? secondary : Dark ? Rgb(255, 172, 184) : Rgb(148, 69, 79),
                new Rect(x, y + 83, 256, 53));
        }

        var footer = Snapshot is { } snapshot
            ? (Error is null ? "" : "数据已过期 · ") + $"Updated {snapshot.FetchedAt.ToLocalTime():HH:mm}"
            : "Waiting for update";
        Write(drawing, footer, 9.5, Error is not null && Snapshot is not null ? Rgb(194, 125, 47) : secondary,
            new Rect(x, card.Bottom - 18, 240, 15));
    }

    private void DrawRow(DrawingContext drawing, string name, QuotaWindow window, double x, double y, Color text)
    {
        Write(drawing, name, 11.5, text, new Rect(x, y, 36, 17));
        var track = new Rect(x + 45, y + 7, 174, 5);
        var palette = PaletteFor(Risk(window, null));
        drawing.DrawRoundedRectangle(Brush(Dark ? Alpha(Rgb(153, 184, 219), .34) : Alpha(Rgb(194, 207, 224), .58)),
            null, track, 2.5, 2.5);
        if (window.RemainingPercent > 0)
        {
            track.Width *= window.RemainingPercent / 100;
            drawing.DrawRoundedRectangle(Gradient(Alpha(Claude ? palette.Primary : Rgb(61, 196, 250), .94),
                Alpha(Claude ? palette.Secondary : Rgb(46, 125, 240), .94)),
                Pen(Alpha(White, .46), .45), track, 2.5, 2.5);
        }
        Write(drawing, Percent(window.RemainingPercent) + "%", 11.5, text,
            new Rect(x + 222, y, 34, 17), TextAlignment.Right);
    }

    private void DrawSourceMark(DrawingContext drawing, Point center, double scale, Color color)
    {
        var pen = Pen(color, .9 * scale);
        if (Claude)
        {
            for (var index = 0; index < 8; index++)
            {
                var angle = index * Math.Tau / 8;
                drawing.DrawLine(pen, Polar(center, angle, 1.2 * scale),
                    Polar(center, angle, (index % 2 == 0 ? 4.2 : 3.4) * scale));
            }
            drawing.DrawEllipse(Brush(color), null, center, 1.1 * scale, 1.1 * scale);
            return;
        }

        for (var index = -1; index <= 1; index++)
        {
            var dy = index * 1.9 * scale;
            var tip = new Point(center.X, center.Y + dy - 1.1 * scale);
            drawing.DrawLine(pen, new Point(center.X - 4.8 * scale, center.Y + dy + 1.3 * scale), tip);
            drawing.DrawLine(pen, tip, new Point(center.X + 4.8 * scale, center.Y + dy + 1.3 * scale));
        }
    }

    private StreamGeometry MakeNeck(Rect orb, double progress, double cardProgress)
    {
        var y = orb.Y + 31;
        const double start = 54;
        var tip = start + (106 - start) * progress;
        var travel = tip - start;
        var pulse = Math.Sin(progress * Math.PI);
        var shoulder = 11 + pulse * 4 + cardProgress * 9;
        var cap = 6 + pulse * 9 + cardProgress * 29;
        var waist = 5 + pulse * 3 + cardProgress * 4;
        var waistX = start + travel * .52;
        var bulge = (5 + pulse * 7) * (1 - cardProgress * .55);
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(new Point(start, y - shoulder), true, true);
        Curve(path, start + travel * .22, y - shoulder - pulse * 3, waistX - travel * .14, y - waist, waistX, y - waist);
        Curve(path, waistX + travel * .16, y - waist, tip - travel * .18, y - cap, tip, y - cap);
        Curve(path, tip + bulge, y - cap * .55, tip + bulge, y + cap * .55, tip, y + cap);
        Curve(path, tip - travel * .18, y + cap, waistX + travel * .16, y + waist, waistX, y + waist);
        Curve(path, waistX - travel * .14, y + waist, start + travel * .22, y + shoulder + pulse * 3, start, y + shoulder);
        Curve(path, start - 4, y + shoulder * .45, start - 4, y - shoulder * .45, start, y - shoulder);
        return geometry;
    }

    private static StreamGeometry MakeCard(Rect orb, Rect card, double progress)
    {
        var center = orb.Y + 31;
        const double x = 92;
        var width = Math.Max(2, card.Width * progress);
        var height = 26 + (card.Height - 26) * progress;
        var y = center - height / 2 + (card.Y + card.Height / 2 - center) * progress;
        var bottom = y + height;
        var right = x + width;
        var radius = Math.Min(20, Math.Min(width, height) / 2);
        var join = Math.Min(Math.Max(8, height / 2 - radius - 4), 10 + progress * 28);
        var upperJoin = Math.Max(y + radius, center - join);
        var lowerJoin = Math.Min(bottom - radius, center + join);
        var orbHalf = 8 + progress * 13;
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(new Point(x + radius, y), true, true);
        path.LineTo(new Point(right - radius, y), true, false);
        Corner(path, right, y + radius, radius);
        path.LineTo(new Point(right, bottom - radius), true, false);
        Corner(path, right - radius, bottom, radius);
        path.LineTo(new Point(x + radius, bottom), true, false);
        Corner(path, x, bottom - radius, radius);
        path.LineTo(new Point(x, lowerJoin), true, false);
        Curve(path, x - 3, center + (lowerJoin - center) * .58, 85, center + orbHalf * .78, 64, center + orbHalf);
        Curve(path, 72, center + orbHalf * .34, 72, center - orbHalf * .34, 64, center - orbHalf);
        Curve(path, 85, center - orbHalf * .78, x - 3, center + (upperJoin - center) * .58, x, upperJoin);
        path.LineTo(new Point(x, y + radius), true, false);
        Corner(path, x + radius, y, radius);
        return geometry;
    }

    private static void Curve(StreamGeometryContext path, double x1, double y1, double x2, double y2, double x, double y) =>
        path.BezierTo(new Point(x1, y1), new Point(x2, y2), new Point(x, y), true, false);

    private static void Corner(StreamGeometryContext path, double x, double y, double radius) =>
        path.ArcTo(new Point(x, y), new System.Windows.Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);

    private void Write(DrawingContext drawing, string value, double size, Color color, Rect region, TextAlignment align = TextAlignment.Left)
    {
        var text = MakeText(value, size, color);
        text.Trimming = TextTrimming.CharacterEllipsis;
        text.TextAlignment = align;
        text.MaxTextWidth = region.Width;
        text.MaxTextHeight = region.Height;
        drawing.DrawText(text, region.Location);
    }

    private FormattedText MakeText(string value, double size, Color color) =>
        new(value, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, Type, size, Brush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static string CompactTokens(long count)
    {
        var billions = count >= 100_000_000;
        var value = count / (billions ? 100_000_000d : 10_000d);
        return Math.Round(value, value >= 100 ? 0 : value >= 10 ? 1 : 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture) + (billions ? "亿" : "万");
    }

    private static string Percent(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static QuotaRisk Risk(QuotaWindow? quota, string? error) => quota is null
        ? error is null ? QuotaRisk.Loading : QuotaRisk.Error
        : quota.RemainingPercent < 20 ? QuotaRisk.Critical
        : quota.RemainingPercent <= 40 || error is not null ? QuotaRisk.Warning : QuotaRisk.Safe;

    private Palette PaletteFor(QuotaRisk risk) => !Claude
        ? new(Rgb(61, 196, 250), Rgb(46, 125, 240), Rgb(171, 235, 255))
        : risk switch
        {
            QuotaRisk.Warning => new(Rgb(224, 107, 222), Rgb(166, 69, 201), Rgb(245, 184, 240)),
            QuotaRisk.Critical => new(Rgb(250, 87, 145), Rgb(194, 43, 110), Rgb(255, 173, 201)),
            QuotaRisk.Error => new(Rgb(214, 74, 125), Rgb(153, 43, 94), Rgb(245, 168, 196)),
            _ => new(Rgb(158, 110, 250), Rgb(110, 69, 219), Rgb(209, 186, 255))
        };

    private readonly record struct Palette(Color Primary, Color Secondary, Color Soft);

    internal static double Smooth(double x) => x * x * (3 - 2 * x);
    private static Point Polar(Point center, double angle, double radius) =>
        new(center.X + Math.Cos(angle) * radius, center.Y - Math.Sin(angle) * radius);
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color Alpha(Color color, double opacity) => Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B);
    private static SolidColorBrush Brush(Color color) => new(color);
    private static LinearGradientBrush Gradient(Color first, Color last, bool vertical = false) =>
        new(first, last, new Point(0, 0), vertical ? new Point(0, 1) : new Point(1, 0));
    private static System.Windows.Media.Pen Pen(Color color, double width) =>
        new(Brush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
}
