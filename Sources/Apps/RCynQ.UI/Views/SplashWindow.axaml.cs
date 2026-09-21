using Avalonia.Controls;

namespace RCynQ.UI.Views;

/// <summary>
/// The splash screen: the icon of the application and a description of what it is loading. It replaces the empty
/// frame the main window would show while the application prepares itself.
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SplashWindow"/> class.
    /// </summary>
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows what the application is doing now.
    /// </summary>
    /// <param name="text">The description, already in the language of the user.</param>
    public void ShowStep(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Step.Text = text;
    }
}
