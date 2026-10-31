using FluentAssertions;
using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools.Tests;

/// <summary>
/// Tests of the templates of the arguments of merge tools.
/// </summary>
public sealed class MergeToolArgumentsTests
{
    /// <summary>The files of a merge, with a space in every path.</summary>
    private static readonly MergeToolFiles _files = new(@"C:\My Work\a.local.cs", @"C:\My Work\a.base.cs", @"C:\My Work\a.incoming.cs", @"C:\My Work\a.cs");

    /// <summary>The titles of the panes.</summary>
    private static readonly MergeToolLabels _labels = new("Local", "Base", "Incoming", "Result: a.cs");

    /// <summary>
    /// Verifies that spaces separate the arguments the user typed and quotation marks keep spaces inside one.
    /// </summary>
    [Fact]
    public void Parse_Splits_By_Spaces_And_Keeps_Quoted_Spaces()
    {
        MergeToolArguments.Parse("  -o {result}  \"--title=My Merge\" {local} \"\" ")
            .Should().Equal("-o", "{result}", "--title=My Merge", "{local}", string.Empty);
    }

    /// <summary>
    /// Verifies that a quotation mark that is not closed makes the text unreadable instead of guessing.
    /// </summary>
    [Fact]
    public void Parse_Refuses_An_Unclosed_Quotation_Mark()
    {
        MergeToolArguments.Parse("{local} \"{result}").Should().BeNull();
        MergeToolArguments.Validate(MergeToolArguments.Parse("{local} \"{result}")).Should().Be(MergeToolTemplateProblem.Unreadable);
    }

    /// <summary>
    /// Verifies that a template written back as text reads back the same.
    /// </summary>
    [Fact]
    public void Format_Reads_Back_The_Same()
    {
        var template = new[] { "/m", "{local}", "--title=My Merge", string.Empty };

        MergeToolArguments.Parse(MergeToolArguments.Format(template)).Should().Equal(template);
    }

    /// <summary>
    /// Verifies what a template must have.
    /// </summary>
    /// <param name="text">The template.</param>
    /// <param name="problem">What is wrong with it.</param>
    [Theory]
    [InlineData("{local} {base} {incoming} {result}", MergeToolTemplateProblem.None)]
    [InlineData("{local} {incoming} -o {result}", MergeToolTemplateProblem.None)]
    [InlineData("/mine:{local} /theirs:{incoming} /merged:{result} /minename:{localLabel}", MergeToolTemplateProblem.None)]
    [InlineData("", MergeToolTemplateProblem.Unreadable)]
    [InlineData("{local} {incoming} {result} {remote}", MergeToolTemplateProblem.UnknownPlaceholder)]
    [InlineData("{base} {incoming} {result}", MergeToolTemplateProblem.MissingLocal)]
    [InlineData("{local} {base} {result}", MergeToolTemplateProblem.MissingIncoming)]
    [InlineData("{local} {base} {incoming}", MergeToolTemplateProblem.MissingResult)]
    public void Validate_Checks_The_Placeholders(string text, MergeToolTemplateProblem problem)
    {
        MergeToolArguments.Validate(MergeToolArguments.Parse(text)).Should().Be(problem);
    }

    /// <summary>
    /// Verifies that a placeholder is filled in as a whole argument and inside one, and that a path with spaces stays
    /// one argument.
    /// </summary>
    [Fact]
    public void Expand_Fills_Whole_And_Partial_Arguments()
    {
        var arguments = MergeToolArguments.Expand(
            new[] { "{incoming}", "/merged:{result}", "-L", "{resultLabel}", "--plain" },
            _files,
            _labels);

        arguments.Should().Equal(@"C:\My Work\a.incoming.cs", @"/merged:C:\My Work\a.cs", "-L", "Result: a.cs", "--plain");
    }

    /// <summary>
    /// Verifies that a placeholder RCynQ does not know is refused instead of being passed on.
    /// </summary>
    [Fact]
    public void Expand_Refuses_An_Unknown_Placeholder()
    {
        var expand = () => MergeToolArguments.Expand(new[] { "{local}", "{MERGED}" }, _files, _labels);

        expand.Should().Throw<ArgumentException>().WithMessage("*{MERGED}*");
    }
}
