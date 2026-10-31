using FluentAssertions;
using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools.Tests;

/// <summary>
/// Tests of the catalog of known merge tools.
/// </summary>
public sealed class MergeToolCatalogTests
{
    /// <summary>
    /// Verifies that every known tool can be used: a valid template, a name Git can choose it by, and somewhere to find it.
    /// </summary>
    [Fact]
    public void Every_Profile_Is_Usable()
    {
        foreach (var profile in MergeToolCatalog.Profiles)
        {
            MergeToolArguments.Validate(profile.Arguments).Should().Be(MergeToolTemplateProblem.None, profile.Id);
            profile.GitNames.Should().NotBeEmpty(profile.Id);
            (profile.Discovery == MergeToolDiscovery.VisualStudio || profile.Commands.Count > 0 || profile.Candidates.Count > 0).Should().BeTrue(profile.Id);
            profile.Id.Should().NotBe(MergeTool.CUSTOM_ID);
        }
    }

    /// <summary>
    /// Verifies that identifiers and Git names point to one tool each.
    /// </summary>
    [Fact]
    public void Identifiers_And_Git_Names_Are_Unique()
    {
        MergeToolCatalog.Profiles.Select(profile => profile.Id).Should().OnlyHaveUniqueItems();
        MergeToolCatalog.Profiles.SelectMany(profile => profile.GitNames).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Verifies that the tools of popular vendors are in the catalog.
    /// </summary>
    [Fact]
    public void Popular_Tools_Are_Known()
    {
        MergeToolCatalog.Profiles.Select(profile => profile.Id)
            .Should().Contain(new[] { "vscode", "vsdiffmerge", "idea", "rider", "kdiff3", "meld", "winmerge", "bc", "p4merge", "tortoisegitmerge" });
    }

    /// <summary>
    /// Verifies that every tool gets the three versions and the place of the result in the order the tool expects them,
    /// the way Git hands them over.
    /// </summary>
    /// <param name="id">The tool.</param>
    /// <param name="expected">The arguments of a merge of <c>L</c>, <c>B</c> and <c>I</c> into <c>R</c>, without the titles.</param>
    [Theory]
    [InlineData("vscode", "--wait --merge I L B R")]
    [InlineData("vsdiffmerge", "I L B R /m")]
    [InlineData("rider", "merge L I B R")]
    [InlineData("kdiff3", "-o R B L I")]
    [InlineData("p4merge", "B I L R")]
    [InlineData("bc", "L I B -mergeoutput=R")]
    public void Tools_Get_The_Files_In_Their_Order(string id, string expected)
    {
        var profile = MergeToolCatalog.Find(id)!;
        var labels = new MergeToolLabels("~", "~", "~", "~");

        var arguments = MergeToolArguments.Expand(profile.Arguments, new MergeToolFiles("L", "B", "I", "R"), labels);

        // The titles are dropped with the switches that introduce them, which leaves the files in their order.
        var files = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            if (index + 1 < arguments.Count && arguments[index + 1] == "~")
            {
                index++;
                continue;
            }

            if (!arguments[index].Contains('~'))
            {
                files.Add(arguments[index]);
            }
        }

        string.Join(' ', files).Should().Be(expected);
    }
}
