using Avalonia.Controls;

namespace RCynQ.UI.Views.Controls;

/// <summary>
/// The buttons that make this computer accept neighbours: install the SSH server or open the TLS port.
/// </summary>
public partial class ServerSetupPanel : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerSetupPanel"/> class.
    /// </summary>
    public ServerSetupPanel()
    {
        InitializeComponent();
    }
}
