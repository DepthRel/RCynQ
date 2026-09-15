using System.Text;

namespace RCynQ.Cli.Output;

/// <summary>
/// Formats rows as left-aligned text columns.
/// </summary>
public sealed class ConsoleTable
{
    /// <summary>The header cells.</summary>
    private readonly string[] _headers;

    /// <summary>The rows.</summary>
    private readonly List<string[]> _rows = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleTable"/> class.
    /// </summary>
    /// <param name="headers">The column headers.</param>
    public ConsoleTable(params string[] headers)
    {
        _headers = headers;
    }

    /// <summary>
    /// Adds a row; missing cells are empty and extra cells are ignored.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <returns>The same table.</returns>
    public ConsoleTable AddRow(params string?[] cells)
    {
        _rows.Add(Enumerable.Range(0, _headers.Length).Select(i => i < cells.Length ? cells[i] ?? string.Empty : string.Empty).ToArray());
        return this;
    }

    /// <summary>
    /// Renders the table with two spaces between columns and a trailing newline.
    /// </summary>
    /// <param name="indent">The indentation of every line.</param>
    /// <returns>The text.</returns>
    public string Render(string indent = "")
    {
        var widths = Enumerable.Range(0, _headers.Length)
            .Select(i => _rows.Select(row => row[i].Length).Append(_headers[i].Length).Max())
            .ToArray();
        var text = new StringBuilder();
        foreach (var row in _rows.Prepend(_headers))
        {
            text.Append(indent).Append(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd()).Append('\n');
        }

        return text.ToString();
    }
}
