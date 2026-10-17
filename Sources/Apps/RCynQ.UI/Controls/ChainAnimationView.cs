using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace RCynQ.UI.Controls;

/// <summary>
/// Draws the chain of a setup: the nodes as rings from left to right, the links between them as lines. A link in
/// progress carries bytes that run along it, the motion of the project page; a finished link is solid green, a failed
/// one red. The motion runs only while a link is in progress and the control is shown.
/// </summary>
public sealed class ChainAnimationView : Avalonia.Controls.Control
{
    /// <summary>Defines the <see cref="Labels"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> LabelsProperty =
        AvaloniaProperty.Register<ChainAnimationView, IReadOnlyList<string>?>(nameof(Labels));

    /// <summary>Defines the <see cref="ActiveLink"/> property.</summary>
    public static readonly StyledProperty<int> ActiveLinkProperty =
        AvaloniaProperty.Register<ChainAnimationView, int>(nameof(ActiveLink), -1);

    /// <summary>Defines the <see cref="AllActive"/> property.</summary>
    public static readonly StyledProperty<bool> AllActiveProperty =
        AvaloniaProperty.Register<ChainAnimationView, bool>(nameof(AllActive));

    /// <summary>Defines the <see cref="CompletedLinks"/> property.</summary>
    public static readonly StyledProperty<int> CompletedLinksProperty =
        AvaloniaProperty.Register<ChainAnimationView, int>(nameof(CompletedLinks));

    /// <summary>Defines the <see cref="FailedLink"/> property.</summary>
    public static readonly StyledProperty<int> FailedLinkProperty =
        AvaloniaProperty.Register<ChainAnimationView, int>(nameof(FailedLink), -1);

    /// <summary>The radius of a node ring.</summary>
    private const double NODE_RADIUS = 17;

    /// <summary>The bytes that run along a link at once.</summary>
    private const int BYTES = 4;

    /// <summary>The seconds one byte takes from one end of a link to the other.</summary>
    private const double SECONDS_PER_RUN = 1.6;

    /// <summary>The colours of the nodes from left to right, the blue and violet of the logo.</summary>
    private static readonly Color[] _nodeColors = { Color.Parse("#60A5FA"), Color.Parse("#9094FB"), Color.Parse("#C084FC") };

    /// <summary>The pen of a link that is waiting.</summary>
    private static readonly IPen _idlePen = new Pen(new SolidColorBrush(Color.Parse("#3A3F47")), 2, new DashStyle(new double[] { 3, 3 }, 0));

    /// <summary>The pen of a finished link.</summary>
    private static readonly IPen _donePen = new Pen(new SolidColorBrush(Color.Parse("#3FB950")), 2.5);

    /// <summary>The pen of a failed link.</summary>
    private static readonly IPen _failedPen = new Pen(new SolidColorBrush(Color.Parse("#F85149")), 2.5);

    /// <summary>The fill of the inside of a ring.</summary>
    private static readonly IBrush _nodeFill = new SolidColorBrush(Color.Parse("#22252A"));

    /// <summary>The colour of the labels.</summary>
    private static readonly IBrush _labelBrush = new SolidColorBrush(Color.Parse("#E6E8EB"));

    /// <summary>The clock of the motion.</summary>
    private readonly TimeProvider _time = TimeProvider.System;

    /// <summary>The timer that redraws the motion, or <see langword="null"/> while nothing moves.</summary>
    private DispatcherTimer? _timer;

    /// <summary>The moment the motion started.</summary>
    private long _started;

    /// <summary>Whether the control is in a visual tree, where it can be seen.</summary>
    private bool _attached;

    /// <summary>
    /// Initializes static members of the <see cref="ChainAnimationView"/> class.
    /// </summary>
    static ChainAnimationView()
    {
        AffectsRender<ChainAnimationView>(LabelsProperty, ActiveLinkProperty, AllActiveProperty, CompletedLinksProperty, FailedLinkProperty);
    }

    /// <summary>Gets or sets the labels of the nodes from left to right.</summary>
    public IReadOnlyList<string>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>Gets or sets the link in progress (0 joins the first two nodes), or -1 for none.</summary>
    public int ActiveLink
    {
        get => GetValue(ActiveLinkProperty);
        set => SetValue(ActiveLinkProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether every link that is neither finished nor failed is in progress.</summary>
    public bool AllActive
    {
        get => GetValue(AllActiveProperty);
        set => SetValue(AllActiveProperty, value);
    }

    /// <summary>Gets or sets how many links from the left are finished.</summary>
    public int CompletedLinks
    {
        get => GetValue(CompletedLinksProperty);
        set => SetValue(CompletedLinksProperty, value);
    }

    /// <summary>Gets or sets the link that failed, or -1 for none.</summary>
    public int FailedLink
    {
        get => GetValue(FailedLinkProperty);
        set => SetValue(FailedLinkProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 480 : availableSize.Width, 86);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ActiveLinkProperty || change.Property == AllActiveProperty || change.Property == IsVisibleProperty)
        {
            UpdateTimer();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        StopTimer();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var labels = Labels;
        if (labels is null || labels.Count == 0 || Bounds.Width <= 0)
        {
            return;
        }

        var centerY = NODE_RADIUS + 6;
        var margin = Math.Min(70, Bounds.Width / (2 * labels.Count));
        var step = labels.Count == 1 ? 0 : (Bounds.Width - (2 * margin)) / (labels.Count - 1);
        var centers = Enumerable.Range(0, labels.Count).Select(i => new Point(margin + (i * step), centerY)).ToArray();

        for (var i = 0; i < centers.Length - 1; i++)
        {
            var from = centers[i] + new Point(NODE_RADIUS + 4, 0);
            var to = centers[i + 1] - new Point(NODE_RADIUS + 4, 0);
            var pen = i == FailedLink ? _failedPen : i < CompletedLinks ? _donePen : _idlePen;
            context.DrawLine(pen, from, to);

            if (i == ActiveLink || (AllActive && i >= CompletedLinks && i != FailedLink))
            {
                DrawBytes(context, from, to);
            }
        }

        for (var i = 0; i < centers.Length; i++)
        {
            var color = _nodeColors[Math.Min(_nodeColors.Length - 1, (int)Math.Round(i * (_nodeColors.Length - 1) / (double)Math.Max(1, centers.Length - 1)))];
            context.DrawEllipse(_nodeFill, new Pen(new SolidColorBrush(color), 4), centers[i], NODE_RADIUS, NODE_RADIUS);

            var text = new FormattedText(labels[i], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12.5, _labelBrush);
            context.DrawText(text, new Point(centers[i].X - (text.Width / 2), centerY + NODE_RADIUS + 10));
        }
    }

    /// <summary>
    /// Draws the bytes on the link in progress, spread evenly and moving from left to right.
    /// </summary>
    /// <param name="context">The drawing context.</param>
    /// <param name="from">The left end of the link.</param>
    /// <param name="to">The right end of the link.</param>
    private void DrawBytes(DrawingContext context, Point from, Point to)
    {
        var elapsed = _time.GetElapsedTime(_started).TotalSeconds;
        for (var i = 0; i < BYTES; i++)
        {
            var phase = ((elapsed / SECONDS_PER_RUN) + (i / (double)BYTES)) % 1.0;
            var at = from + ((to - from) * phase);

            // A byte fades in at the start and out at the end, so the run looks continuous.
            var opacity = Math.Sin(phase * Math.PI);
            var color = Color.FromArgb((byte)(255 * opacity), 0x90, 0x94, 0xFB);
            context.DrawRectangle(new SolidColorBrush(color), null, new Rect(at.X - 4, at.Y - 4, 8, 8), 2, 2);
        }
    }

    /// <summary>
    /// Starts the motion while a link is in progress and the control is shown, and stops it otherwise.
    /// </summary>
    private void UpdateTimer()
    {
        var moving = (ActiveLink >= 0 || AllActive) && IsVisible && _attached;
        if (!moving)
        {
            StopTimer();
            return;
        }

        if (_timer is not null)
        {
            return;
        }

        _started = _time.GetTimestamp();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => InvalidateVisual());
        _timer.Start();
    }

    /// <summary>
    /// Stops the motion.
    /// </summary>
    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }
}
