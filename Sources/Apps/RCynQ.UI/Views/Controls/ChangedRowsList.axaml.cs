using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.Views.Controls;

/// <summary>
/// The rows of a list of changed files: files and, in a tree, folders. The tab of the synchronization and the dialog that
/// applies a patch show them the same way. A double click on a file shows its diff where the list offers it, and leaves
/// the check mark of the file as it was.
/// </summary>
public partial class ChangedRowsList : UserControl
{
    /// <summary>The property of <see cref="Rows"/>.</summary>
    public static readonly StyledProperty<IEnumerable?> RowsProperty = AvaloniaProperty.Register<ChangedRowsList, IEnumerable?>(nameof(Rows));

    /// <summary>
    /// The file the last single click pressed and whether it was checked before that click, so that the second click of
    /// a double click can undo what the first one did to the check mark.
    /// </summary>
    private (ChangedFileItemViewModel File, bool WasSelected)? _firstPress;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangedRowsList"/> class.
    /// </summary>
    public ChangedRowsList()
    {
        InitializeComponent();

        // Before the check box of the row: the second press of a double click must not reach it.
        List.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Gets or sets the rows, top to bottom.</summary>
    public IEnumerable? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowsProperty)
        {
            List.ItemsSource = Rows;
        }
    }

    /// <summary>
    /// Remembers the file a single click presses, and turns the second press of a double click into showing the diff of
    /// the file: the check mark gets back the state it had before the first click, and the check box never sees the
    /// second one.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event arguments.</param>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var first = _firstPress;
        _firstPress = null;
        if (!e.GetCurrentPoint(List).Properties.IsLeftButtonPressed ||
            (e.Source as StyledElement)?.DataContext is not ChangedFileItemViewModel file)
        {
            return;
        }

        if (e.ClickCount % 2 == 1)
        {
            _firstPress = (file, file.IsSelected);
            return;
        }

        if (!file.ShowDiffCommand.CanExecute(null))
        {
            return;
        }

        if (first is { } press && ReferenceEquals(press.File, file))
        {
            file.IsSelected = press.WasSelected;
        }

        e.Handled = true;
        file.ShowDiffCommand.Execute(null);
    }
}
