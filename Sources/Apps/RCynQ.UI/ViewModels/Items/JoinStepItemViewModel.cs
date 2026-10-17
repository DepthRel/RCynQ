using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One stage of joining as the wizard shows it: running while the service works on it, then with its outcome.
/// </summary>
public sealed partial class JoinStepItemViewModel : ObservableObject
{
    /// <summary>The message of the stage, empty while it runs.</summary>
    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>Whether the stage succeeded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    private bool _succeeded;

    /// <summary>Whether the stage still runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    private bool _isRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinStepItemViewModel"/> class for a stage that ended.
    /// </summary>
    /// <param name="localizer">The localizer of the name of the stage.</param>
    /// <param name="step">How the stage ended.</param>
    public JoinStepItemViewModel(ILocalizer localizer, JoinStepResult step)
        : this(localizer, step?.Step ?? throw new ArgumentNullException(nameof(step)))
    {
        Finish(step.Succeeded, step.Message);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinStepItemViewModel"/> class for a stage that runs.
    /// </summary>
    /// <param name="localizer">The localizer of the name of the stage.</param>
    /// <param name="step">The stage.</param>
    public JoinStepItemViewModel(ILocalizer localizer, JoinStep step)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        Step = step;
        Title = localizer.Get("JoinStep." + step);
        IsRunning = true;
    }

    /// <summary>Gets the stage.</summary>
    public JoinStep Step { get; }

    /// <summary>Gets the name of the stage.</summary>
    public string Title { get; }

    /// <summary>Gets a value indicating whether the stage ended and failed.</summary>
    public bool HasFailed => !IsRunning && !Succeeded;

    /// <summary>
    /// Records how the stage ended.
    /// </summary>
    /// <param name="succeeded">Whether it succeeded.</param>
    /// <param name="message">Its message.</param>
    public void Finish(bool succeeded, string? message)
    {
        Succeeded = succeeded;
        Message = message ?? string.Empty;
        IsRunning = false;
    }
}
