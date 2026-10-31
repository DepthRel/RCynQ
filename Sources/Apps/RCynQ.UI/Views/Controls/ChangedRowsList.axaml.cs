using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace RCynQ.UI.Views.Controls;

/// <summary>
/// The rows of a list of changed files: files and, in a tree, folders. The tab of the synchronization and the dialog that
/// applies a patch show them the same way.
/// </summary>
public partial class ChangedRowsList : UserControl
{
    /// <summary>The property of <see cref="Rows"/>.</summary>
    public static readonly StyledProperty<IEnumerable?> RowsProperty = AvaloniaProperty.Register<ChangedRowsList, IEnumerable?>(nameof(Rows));

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangedRowsList"/> class.
    /// </summary>
    public ChangedRowsList()
    {
        InitializeComponent();
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
}
