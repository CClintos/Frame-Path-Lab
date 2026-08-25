using System.Text;

namespace FramePathLab.Core.Analysis;

internal static class BoundedCsvReader
{
    public static IReadOnlyList<string> ParseLine(
        string line,
        int maximumColumns,
        int maximumCellCharacters)
    {
        ArgumentNullException.ThrowIfNull(line);
        var cells = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        AppendBounded(builder, '"', maximumCellCharacters);
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    AppendBounded(builder, character, maximumCellCharacters);
                }

                continue;
            }

            switch (character)
            {
                case '"' when builder.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    AddCell(cells, builder, maximumColumns);
                    break;
                default:
                    AppendBounded(builder, character, maximumCellCharacters);
                    break;
            }
        }

        if (quoted)
        {
            throw new InvalidDataException("Multiline or unterminated quoted CSV fields are not supported.");
        }

        AddCell(cells, builder, maximumColumns);
        return cells;
    }

    private static void AddCell(List<string> cells, StringBuilder builder, int maximumColumns)
    {
        if (cells.Count >= maximumColumns)
        {
            throw new InvalidDataException($"CSV exceeds the {maximumColumns}-column limit.");
        }

        cells.Add(builder.ToString());
        builder.Clear();
    }

    private static void AppendBounded(StringBuilder builder, char value, int maximumCellCharacters)
    {
        if (builder.Length >= maximumCellCharacters)
        {
            throw new InvalidDataException($"CSV cell exceeds the {maximumCellCharacters}-character limit.");
        }

        builder.Append(value);
    }
}
