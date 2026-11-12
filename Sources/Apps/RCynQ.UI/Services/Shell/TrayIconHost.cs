using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using RCynQ.Localization;

namespace RCynQ.UI.Services.Shell;

/// <summary>
/// The icon of the application in the notification area: a click on it, or "Open" in its menu, shows the main window;
/// "Exit" in its menu ends the application together with the service it started.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    /// <summary>
    /// The picture of the icon: the Windows icon file on Windows, the picture other desktops draw best elsewhere.
    /// </summary>
    private static readonly Uri _iconUri = new(OperatingSystem.IsWindows() ? "avares://RCynQ.UI/Assets/rcynq.ico" : "avares://RCynQ.UI/Assets/rcynq-256.png");

    /// <summary>
    /// The application that shows the icon.
    /// </summary>
    private readonly Application _application;

    /// <summary>
    /// The localizer of the texts of the menu.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// The icon.
    /// </summary>
    private readonly TrayIcon _icon;

    /// <summary>
    /// The menu item that shows the main window.
    /// </summary>
    private readonly NativeMenuItem _open;

    /// <summary>
    /// The menu item that ends the application.
    /// </summary>
    private readonly NativeMenuItem _exit;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayIconHost"/> class and shows the icon.
    /// </summary>
    /// <param name="application">The application that shows the icon.</param>
    /// <param name="localizer">The localizer of the texts of the menu.</param>
    /// <param name="show">Shows the main window.</param>
    /// <param name="exit">Ends the application.</param>
    public TrayIconHost(Application application, ILocalizer localizer, Action show, Action exit)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(exit);

        _open = new NativeMenuItem();
        _open.Click += (_, _) => show();
        _exit = new NativeMenuItem();
        _exit.Click += (_, _) => exit();

        var menu = new NativeMenu();
        menu.Items.Add(_open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_exit);

        using var picture = AssetLoader.Open(_iconUri);
        _icon = new TrayIcon { Icon = new WindowIcon(picture), ToolTipText = "RCynQ", Menu = menu };
        _icon.Clicked += (_, _) => show();
        ApplyTexts();

        TrayIcon.SetIcons(_application, new TrayIcons { _icon });
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Changes the hint shown over the icon, for example to tell about a new version.
    /// </summary>
    /// <param name="text">The hint.</param>
    public void SetToolTip(string text) => _icon.ToolTipText = string.IsNullOrWhiteSpace(text) ? "RCynQ" : text;

    /// <inheritdoc />
    public void Dispose()
    {
        _localizer.LanguageChanged -= OnLanguageChanged;
        TrayIcon.SetIcons(_application, new TrayIcons());
        _icon.Dispose();
    }

    /// <summary>
    /// Puts the menu into the new language, on the UI thread.
    /// </summary>
    /// <param name="sender">The localizer.</param>
    /// <param name="e">The event arguments.</param>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyTexts();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyTexts);
        }
    }

    /// <summary>
    /// Writes the texts of the menu in the current language.
    /// </summary>
    private void ApplyTexts()
    {
        _open.Header = _localizer.Get("Tray.Open");
        _exit.Header = _localizer.Get("Tray.Exit");
    }
}
