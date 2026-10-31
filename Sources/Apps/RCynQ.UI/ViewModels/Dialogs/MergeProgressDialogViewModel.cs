using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Merging;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The window of a merge in a merge tool: which file is open, what the merge waits for, the questions it asks, and the
/// way to give the merge up. It runs the merge while it is open and closes when the merge ends.
/// </summary>
public sealed partial class MergeProgressDialogViewModel : DialogViewModelBase, IMergeProgress
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Runs the merge.</summary>
    private readonly Func<IMergeProgress, CancellationToken, Task<ExternalMergeOutcome>> _work;

    /// <summary>Cancelled when the user gives the merge up.</summary>
    private readonly CancellationTokenSource _cancel = new();

    /// <summary>The question that waits for an answer, or <see langword="null"/>.</summary>
    private TaskCompletionSource<MergePromptChoice>? _question;

    /// <summary>The file that is open in the tool.</summary>
    [ObservableProperty]
    private string _fileText = string.Empty;

    /// <summary>What the merge does now.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>The question that waits for an answer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    private string _questionText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeProgressDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="toolName">The name of the merge tool.</param>
    /// <param name="work">Runs the merge with this window as its progress and the token of giving it up.</param>
    public MergeProgressDialogViewModel(ILocalizer localizer, string toolName, Func<IMergeProgress, CancellationToken, Task<ExternalMergeOutcome>> work)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _work = work ?? throw new ArgumentNullException(nameof(work));
        Title = _localizer.Format("MergeTool.WindowTitle", toolName);
    }

    /// <summary>Gets the title of the window.</summary>
    public string Title { get; }

    /// <summary>Gets the answers to the question that waits.</summary>
    public ObservableCollection<MergePromptOptionViewModel> Options { get; } = new();

    /// <summary>Gets a value indicating whether a question waits for an answer.</summary>
    public bool IsAsking => QuestionText.Length > 0;

    /// <summary>Gets a value indicating whether the merge has ended.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Gets how the merge ended, or <see langword="null"/> while it runs.</summary>
    public ExternalMergeOutcome? Outcome { get; private set; }

    /// <summary>
    /// Runs the merge and closes the window when it ends. The window calls it once it is open.
    /// </summary>
    /// <returns>A task that completes when the merge has ended.</returns>
    public async Task RunAsync()
    {
        try
        {
            Outcome = await _work(this, _cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Outcome = ExternalMergeOutcome.Cancelled;
        }
        catch (Exception ex) when (ex is ServiceApiException or HttpRequestException)
        {
            Outcome = ExternalMergeOutcome.Failed(ex is ServiceApiException ? ex.Message : _localizer.Describe(ex));
        }

        IsFinished = true;
        Close(true);
    }

    /// <inheritdoc />
    public void ShowFile(int number, int count, string path) => FileText = _localizer.Format("MergeTool.File", number, count, path);

    /// <inheritdoc />
    public void ShowStatus(string text) => StatusText = text;

    /// <inheritdoc />
    public Task<MergePromptChoice> AskAsync(string message, IReadOnlyList<MergePromptChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (_cancel.IsCancellationRequested)
        {
            return Task.FromResult(MergePromptChoice.Cancel);
        }

        _question = new TaskCompletionSource<MergePromptChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Options.Clear();
        foreach (var choice in choices.Where(choice => choice != MergePromptChoice.Cancel))
        {
            Options.Add(new MergePromptOptionViewModel(choice, TextOf(choice), Answer));
        }

        QuestionText = message;
        return _question.Task;
    }

    /// <summary>
    /// Gives the merge up: stops waiting for the tool, which stays open, and for an answer.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _cancel.Cancel();
        Answer(MergePromptChoice.Cancel);
    }

    /// <summary>
    /// Gets the text of the button of an answer.
    /// </summary>
    /// <param name="choice">The answer.</param>
    /// <returns>The text.</returns>
    private string TextOf(MergePromptChoice choice) => _localizer.Get(choice switch
    {
        MergePromptChoice.CheckAgain => "MergeTool.Choice.CheckAgain",
        MergePromptChoice.OpenAgain => "MergeTool.Choice.OpenAgain",
        MergePromptChoice.KeepMarkers => "MergeTool.Choice.KeepMarkers",
        MergePromptChoice.Retry => "MergeTool.Choice.Retry",
        _ => "MergeTool.CancelMerge"
    });

    /// <summary>
    /// Answers the question that waits.
    /// </summary>
    /// <param name="choice">The answer.</param>
    private void Answer(MergePromptChoice choice)
    {
        var question = _question;
        _question = null;
        Options.Clear();
        QuestionText = string.Empty;
        question?.TrySetResult(choice);
    }
}
