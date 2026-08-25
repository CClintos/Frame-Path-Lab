namespace FramePathLab.Core.Statistics;

public static class DescriptiveStatistics
{
    public static double Mean(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var sum = 0d;
        var compensation = 0d;
        foreach (var value in values)
        {
            var adjusted = value - compensation;
            var next = sum + adjusted;
            compensation = (next - sum) - adjusted;
            sum = next;
        }

        return sum / values.Count;
    }

    public static double QuantileR7(IReadOnlyList<double> sortedValues, double probability)
    {
        ArgumentNullException.ThrowIfNull(sortedValues);
        if (sortedValues.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(sortedValues));
        }

        if (probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var index = (sortedValues.Count - 1) * probability;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var fraction = index - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * fraction);
    }

    public static double SampleStandardDeviation(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            return 0;
        }

        var mean = Mean(values);
        var sum = values.Sum(value => Math.Pow(value - mean, 2));
        return Math.Sqrt(sum / (values.Count - 1));
    }
}
