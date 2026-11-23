using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OptiPulse.Core.Models;

namespace OptiPulse.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && (Invert ? v != Visibility.Visible : v == Visibility.Visible);
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

public sealed class IntEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i && parameter is not null && int.TryParse(parameter.ToString(), out var target))
        {
            return i == target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class IntEqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i && parameter is not null && int.TryParse(parameter.ToString(), out var target))
        {
            return i == target;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is not null && int.TryParse(parameter.ToString(), out var target))
        {
            return target;
        }
        return Binding.DoNothing;
    }
}

public sealed class NoticeLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0x4F, 0x7C, 0xFF));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TweakNoticeLevel level ? level switch
        {
            TweakNoticeLevel.Warning => WarningBrush,
            TweakNoticeLevel.Info => InfoBrush,
            _ => Brushes.Transparent
        } : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class NoticeLevelToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TweakNoticeLevel level && level != TweakNoticeLevel.None
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class IconKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && !string.IsNullOrWhiteSpace(key))
        {
            return Application.Current?.TryFindResource(key) as Geometry;
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class ImpactToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RecommendationImpact impact ? impact switch
        {
            RecommendationImpact.High => "Высокий эффект",
            RecommendationImpact.Medium => "Средний эффект",
            _ => "Низкий эффект"
        } : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImpactToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush HighBg = new(Color.FromArgb(0x2A, 0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush MedBg = new(Color.FromArgb(0x2A, 0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush LowBg = new(Color.FromArgb(0x2A, 0x4F, 0x7C, 0xFF));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RecommendationImpact impact ? impact switch
        {
            RecommendationImpact.High => HighBg,
            RecommendationImpact.Medium => MedBg,
            _ => LowBg
        } : LowBg;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ImpactToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush HighFg = new(Color.FromRgb(0xFC, 0xA5, 0xA5));
    private static readonly SolidColorBrush MedFg = new(Color.FromRgb(0xFC, 0xD3, 0x4D));
    private static readonly SolidColorBrush LowFg = new(Color.FromRgb(0x93, 0xC5, 0xFD));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RecommendationImpact impact ? impact switch
        {
            RecommendationImpact.High => HighFg,
            RecommendationImpact.Medium => MedFg,
            _ => LowFg
        } : LowFg;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SafetyToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RecommendationSafety safety ? safety switch
        {
            RecommendationSafety.Safe => "Безопасно",
            RecommendationSafety.Moderate => "Умеренно",
            _ => "Осторожно"
        } : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SafetyToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush SafeFg = new(Color.FromRgb(0x34, 0xE5, 0x9E));
    private static readonly SolidColorBrush ModFg = new(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly SolidColorBrush CautFg = new(Color.FromRgb(0xF8, 0x71, 0x71));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RecommendationSafety safety ? safety switch
        {
            RecommendationSafety.Safe => SafeFg,
            RecommendationSafety.Moderate => ModFg,
            _ => CautFg
        } : SafeFg;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0x93, 0xC5, 0xFD));
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x34, 0xE5, 0x9E));
    private static readonly SolidColorBrush WarnBrush = new(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly SolidColorBrush ErrBrush = new(Color.FromRgb(0xF8, 0x71, 0x71));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LogLevel level ? level switch
        {
            LogLevel.Success => SuccessBrush,
            LogLevel.Warning => WarnBrush,
            LogLevel.Error => ErrBrush,
            _ => InfoBrush
        } : InfoBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ScoreToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Good = new(Color.FromRgb(0x34, 0xE5, 0x9E));
    private static readonly SolidColorBrush Mid = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush Low = new(Color.FromRgb(0xEF, 0x44, 0x44));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int score)
        {
            return score >= 75 ? Good : score >= 45 ? Mid : Low;
        }
        return Mid;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
