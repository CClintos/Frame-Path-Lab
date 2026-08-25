using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FramePathLab.Core.Models;

namespace FramePathLab.App.Converters;

public sealed class DispositionBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FindingDisposition disposition
            ? disposition switch
            {
                FindingDisposition.NoAction => new SolidColorBrush(Color.FromRgb(88, 214, 141)),
                FindingDisposition.Measure => new SolidColorBrush(Color.FromRgb(101, 211, 255)),
                FindingDisposition.GuidedExperiment => new SolidColorBrush(Color.FromRgb(255, 196, 92)),
                FindingDisposition.ExplainOnly => new SolidColorBrush(Color.FromRgb(170, 185, 205)),
                FindingDisposition.Unsupported => new SolidColorBrush(Color.FromRgb(255, 153, 102)),
                FindingDisposition.Excluded => new SolidColorBrush(Color.FromRgb(255, 105, 120)),
                _ => Brushes.LightGray
            }
            : Brushes.LightGray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
