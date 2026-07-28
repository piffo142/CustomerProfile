using System.Globalization;
using SIG.ClientCard.App.Services;

namespace SIG.ClientCard.App.Converters;

/// <summary>Relative attachment path → local file ImageSource (local file is the source of truth).</summary>
public sealed class AttachmentImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string relative || relative.Length == 0)
        {
            return null;
        }

        var absolute = AttachmentService.GetAbsolutePath(relative);
        return File.Exists(absolute) ? ImageSource.FromFile(absolute) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
