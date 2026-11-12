using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RCynQ.UI.Views.Pages;

/// <summary>
/// The settings tab.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsView"/> class.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();

        // The box of the days takes digits only: anything else typed never reaches it. Pasted text is filtered by the
        // view model.
        RetentionDaysBox.AddHandler(TextInputEvent, OnRetentionDaysInput, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Drops typed text that is not made of digits.
    /// </summary>
    /// <param name="sender">The box of the days.</param>
    /// <param name="e">The typed text.</param>
    private static void OnRetentionDaysInput(object? sender, TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text) && !e.Text.All(char.IsAsciiDigit))
        {
            e.Handled = true;
        }
    }
}
