using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Horizon.Core.Models;

namespace Horizon.App;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility visibility && visibility != Visibility.Visible;
}

public sealed class EqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Length >= 2 && Equals(values[0], values[1]);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => targetTypes.Select(_ => Binding.DoNothing).ToArray();
}

public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            StatusTone.Success or TweakApplyState.Applied => "Brush.Success",
            StatusTone.Warning or TweakRisk.Advanced or TweakRisk.Medium => "Brush.Warning",
            StatusTone.Danger or TweakApplyState.Failed => "Brush.Danger",
            _ => "Brush.TextSecondary"
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class LessThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!double.TryParse(value?.ToString(), NumberStyles.Float, culture, out var current)) return false;
        return double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) && current < limit;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DetailColumnWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? new GridLength(0) : new GridLength(42, GridUnitType.Star);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class MaterialIconConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, int> CodePoints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = 0xE88A, ["auto_awesome"] = 0xE65F, ["tune"] = 0xE429, ["sports_esports"] = 0xEA28,
        ["delete_sweep"] = 0xE16C, ["rocket_launch"] = 0xEB9B, ["cleaning_services"] = 0xF0FF,
        ["memory"] = 0xE322, ["memory_alt"] = 0xF7A3, ["developer_board"] = 0xE30A, ["hard_drive"] = 0xF80E,
        ["verified_user"] = 0xE8E8, ["speed"] = 0xE9E4, ["restore"] = 0xE8B3, ["history"] = 0xE889,
        ["workspace_premium"] = 0xE7AF, ["settings"] = 0xE8B8, ["help_outline"] = 0xE8FD,
        ["bolt"] = 0xEA0B, ["check_circle"] = 0xE86C, ["blur_on"] = 0xE3A5, ["bug_report"] = 0xE868,
        ["folder_delete"] = 0xEB34, ["search"] = 0xE8B6, ["filter_list"] = 0xE152, ["chevron_right"] = 0xE5CC,
        ["arrow_forward"] = 0xE5C8, ["info"] = 0xE88E, ["check"] = 0xE5CA, ["shield"] = 0xE9F8
    };
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Glyph(value?.ToString());
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    public static string Glyph(string? name) => name is not null && CodePoints.TryGetValue(name, out var codePoint) ? char.ConvertFromUtf32(codePoint) : "•";
}
