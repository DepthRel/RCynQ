using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Localization;
using RCynQ.UI.Services.Localization;

namespace RCynQ.UI.ViewModels;

/// <summary>
/// The language selector: the languages offered and the one in use. One instance is shared by every place that shows
/// the selector, so they always agree.
/// </summary>
public sealed partial class LanguageViewModel : ObservableObject
{
    /// <summary>
    /// Applies and stores the choice.
    /// </summary>
    private readonly ILanguageService _service;

    /// <summary>
    /// The language in use.
    /// </summary>
    [ObservableProperty]
    private LanguageOption _selected;

    /// <summary>
    /// The problem that prevented storing the choice, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageViewModel"/> class.
    /// </summary>
    /// <param name="service">The language service, already initialized: the selector shows the language in use.</param>
    /// <param name="flags">Finds the flag of a language by its code, or <see langword="null"/> to show no flags.</param>
    public LanguageViewModel(ILanguageService service, Func<string, IImage?>? flags = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Options = [.. service.Languages.Select(language => new LanguageOption(language, flags?.Invoke(language.Code)))];
        _selected = Options.FirstOrDefault(option => option.Code == service.Current.Code) ?? Options[0];
    }

    /// <summary>
    /// Gets the languages offered.
    /// </summary>
    public IReadOnlyList<LanguageOption> Options { get; }

    /// <summary>
    /// Gets a value indicating whether a problem is shown.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// Switches the interface to the language the user picked and stores the choice.
    /// </summary>
    /// <param name="value">The picked language.</param>
    partial void OnSelectedChanged(LanguageOption value)
    {
        if (value is null || value.Code == _service.Current.Code)
        {
            return;
        }

        Apply(value);
    }

    /// <summary>
    /// Applies a language; a failure to store it does not undo the switch, the language is used until the end of the
    /// session and the user is told.
    /// </summary>
    /// <param name="option">The language.</param>
    private void Apply(LanguageOption option)
    {
        try
        {
            _service.Select(option.Code);
            Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = Texts.Format("Language.Error", ex.Message);
        }
    }
}
