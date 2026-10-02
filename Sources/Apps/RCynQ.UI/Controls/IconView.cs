using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace RCynQ.UI.Controls;

/// <summary>
/// Draws an outline icon defined on a 24x24 grid, scaled to the control size with a constant stroke thickness.
/// </summary>
public sealed class IconView : Control
{
    /// <summary>Defines the <see cref="Data"/> property.</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<IconView, Geometry?>(nameof(Data));

    /// <summary>Defines the <see cref="IconKey"/> property.</summary>
    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<IconView, string?>(nameof(IconKey));

    /// <summary>Defines the <see cref="Foreground"/> property (inherited like text foreground).</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<IconView>();

    /// <summary>Defines the <see cref="StrokeThickness"/> property.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(StrokeThickness), 1.75);

    /// <summary>The size of the icon grid.</summary>
    private const double GRID_SIZE = 24;

    /// <summary>
    /// Initializes static members of the <see cref="IconView"/> class.
    /// </summary>
    static IconView()
    {
        AffectsRender<IconView>(DataProperty, IconKeyProperty, ForegroundProperty, StrokeThicknessProperty);
    }

    /// <summary>Gets or sets the icon geometry; takes precedence over <see cref="IconKey"/>.</summary>
    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Gets or sets the resource key of the icon geometry.</summary>
    public string? IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    /// <summary>Gets or sets the stroke brush.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Gets or sets the stroke thickness in device-independent pixels.</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var geometry = Data ?? ResolveGeometry();
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (geometry is null || size <= 0 || Foreground is null)
        {
            return;
        }

        var scale = size / GRID_SIZE;
        var offsetX = (Bounds.Width - size) / 2;
        var offsetY = (Bounds.Height - size) / 2;
        var pen = new Pen(Foreground, StrokeThickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>
    /// Looks up the geometry named by <see cref="IconKey"/>.
    /// </summary>
    /// <returns>The geometry, or <see langword="null"/>.</returns>
    private Geometry? ResolveGeometry()
        => IconKey is { Length: > 0 } key && this.TryFindResource(key, out var value) ? value as Geometry : null;
}
