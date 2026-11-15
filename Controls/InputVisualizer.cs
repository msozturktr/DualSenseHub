using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DualSenseHub.Core;

namespace DualSenseHub.Controls;

/// <summary>Draws live controller input: buttons, sticks, trigger bars and touchpad points.</summary>
public sealed class InputVisualizer : Control
{
    public static readonly StyledProperty<InputState?> StateProperty =
        AvaloniaProperty.Register<InputVisualizer, InputState?>(nameof(State));

    static InputVisualizer() => AffectsRender<InputVisualizer>(StateProperty);

    public InputState? State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    private static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#3A3F55"));
    private static readonly IBrush On = new SolidColorBrush(Color.Parse("#4CC2FF"));
    private static readonly IBrush Panel = new SolidColorBrush(Color.Parse("#1E2130"));
    private static readonly IBrush Text = new SolidColorBrush(Color.Parse("#C9CFE8"));
    private static readonly Pen Outline = new(new SolidColorBrush(Color.Parse("#5B638A")), 1.5);

    protected override Size MeasureOverride(Size availableSize) => new(560, 320);

    public override void Render(DrawingContext ctx)
    {
        var s = State;
        var b = s?.Buttons ?? Buttons.None;
        IBrush Br(Buttons f) => (b & f) != 0 ? On : Idle;

        ctx.DrawRectangle(Panel, null, new Rect(0, 0, 560, 320), 10, 10);

        // Triggers and shoulders
        Bar(ctx, new Rect(40, 14, 110, 14), (s?.L2 ?? 0) / 255.0, "L2");
        Bar(ctx, new Rect(410, 14, 110, 14), (s?.R2 ?? 0) / 255.0, "R2");
        ctx.DrawRectangle(Br(Buttons.L1), null, new Rect(40, 38, 110, 16), 6, 6); Label(ctx, "L1", 85, 38);
        ctx.DrawRectangle(Br(Buttons.R1), null, new Rect(410, 38, 110, 16), 6, 6); Label(ctx, "R1", 455, 38);

        // D-pad
        ctx.DrawRectangle(Br(Buttons.DpadUp), null, new Rect(70, 80, 24, 28), 4, 4);
        ctx.DrawRectangle(Br(Buttons.DpadDown), null, new Rect(70, 134, 24, 28), 4, 4);
        ctx.DrawRectangle(Br(Buttons.DpadLeft), null, new Rect(40, 108, 30, 26), 4, 4);
        ctx.DrawRectangle(Br(Buttons.DpadRight), null, new Rect(94, 108, 30, 26), 4, 4);

        // Face buttons
        Circle(ctx, Br(Buttons.Triangle), 460, 90, 16, "Y");
        Circle(ctx, Br(Buttons.Cross), 460, 154, 16, "X");
        Circle(ctx, Br(Buttons.Square), 428, 122, 16, "K");
        Circle(ctx, Br(Buttons.Circle), 492, 122, 16, "D");

        // Center buttons
        ctx.DrawRectangle(Br(Buttons.Share), null, new Rect(150, 70, 26, 10), 4, 4);
        ctx.DrawRectangle(Br(Buttons.Options), null, new Rect(384, 70, 26, 10), 4, 4);
        Circle(ctx, Br(Buttons.PS), 280, 214, 11, "");
        Circle(ctx, Br(Buttons.Mic), 280, 242, 7, "");

        // Touchpad
        var pad = new Rect(190, 90, 180, 100);
        ctx.DrawRectangle(Br(Buttons.Touchpad), Outline, pad, 8, 8);
        if (s != null) { Dot(ctx, pad, s.Touch1, Brushes.Orange); Dot(ctx, pad, s.Touch2, Brushes.LimeGreen); }

        // Sticks
        Stick(ctx, 150, 240, s?.Lx, s?.Ly, Br(Buttons.L3));
        Stick(ctx, 410, 240, s?.Rx, s?.Ry, Br(Buttons.R3));
    }

    private static void Bar(DrawingContext ctx, Rect r, double v, string label)
    {
        ctx.DrawRectangle(Idle, null, r, 4, 4);
        if (v > 0) ctx.DrawRectangle(On, null, new Rect(r.X, r.Y, r.Width * v, r.Height), 4, 4);
        Label(ctx, label, r.X + r.Width / 2, r.Y - 1);
    }

    private static void Circle(DrawingContext ctx, IBrush fill, double x, double y, double r, string label)
    {
        ctx.DrawEllipse(fill, Outline, new Point(x, y), r, r);
        if (label.Length > 0) Label(ctx, label, x, y - 8);
    }

    private static void Stick(DrawingContext ctx, double cx, double cy, byte? x, byte? y, IBrush fill)
    {
        const double R = 44;
        ctx.DrawEllipse(Panel, Outline, new Point(cx, cy), R, R);
        double dx = ((x ?? 128) - 127.5) / 127.5 * (R - 12), dy = ((y ?? 128) - 127.5) / 127.5 * (R - 12);
        ctx.DrawEllipse(fill, null, new Point(cx + dx, cy + dy), 12, 12);
    }

    private static void Dot(DrawingContext ctx, Rect pad, TouchPoint p, IBrush brush)
    {
        if (!p.Active) return;
        double px = pad.X + p.X / (double)InputState.TouchWidth * pad.Width;
        double py = pad.Y + p.Y / (double)InputState.TouchHeight * pad.Height;
        ctx.DrawEllipse(brush, null, new Point(px, py), 7, 7);
    }

    private static void Label(DrawingContext ctx, string text, double cx, double top)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Text);
        ctx.DrawText(ft, new Point(cx - ft.Width / 2, top));
    }
}
