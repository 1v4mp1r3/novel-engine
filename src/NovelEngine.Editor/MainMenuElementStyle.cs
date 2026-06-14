using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovelEngine.Core;

namespace NovelEngine.Editor;

internal sealed class MainMenuElementStyle
{
    private static readonly BrushConverter BrushConverter = new();

    private MainMenuElementStyle()
    {
    }

    public required Brush Foreground { get; init; }
    public required Brush Background { get; init; }
    public required Brush Border { get; init; }
    public required double FontSize { get; init; }
    public required FontWeight FontWeight { get; init; }
    public required FontStyle FontStyle { get; init; }
    public required TextAlignment TextAlignment { get; init; }
    public required CornerRadius CornerRadius { get; init; }
    public required double Opacity { get; init; }

    public static MainMenuElementStyle From(
        MainMenuElement element,
        Color backgroundFallback,
        Color borderFallback)
    {
        var declarations = ParseDeclarations(element.CustomStyleCode);
        var legacyCode = element.CustomStyleCode;
        return new MainMenuElementStyle
        {
            Foreground = ParseBrush(
                Get(declarations, "foreground", "text", "textcolor", "color") ?? element.Foreground,
                Colors.White),
            Background = ParseBrush(
                Get(declarations, "background", "backgroundcolor", "fill") ?? element.Background,
                backgroundFallback),
            Border = ParseBrush(
                Get(declarations, "border", "bordercolor", "stroke") ?? element.Border,
                borderFallback),
            FontSize = Math.Clamp(
                ParseDouble(Get(declarations, "fontsize"), element.FontSize),
                6,
                120),
            FontWeight = ParseFontWeight(
                Get(declarations, "fontweight", "weight"),
                legacyCode),
            FontStyle = ParseFontStyle(
                Get(declarations, "fontstyle", "style"),
                legacyCode),
            TextAlignment = ParseTextAlignment(Get(declarations, "textalign", "align")),
            CornerRadius = ParseCornerRadius(Get(declarations, "cornerradius", "radius")),
            Opacity = Math.Clamp(ParseDouble(Get(declarations, "opacity"), 1), 0, 1),
        };
    }

    private static Dictionary<string, string> ParseDeclarations(string code)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPart in code.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            var separator = part.IndexOf('=');
            if (separator < 0)
            {
                separator = part.IndexOf(':');
            }
            if (separator <= 0 || separator >= part.Length - 1)
            {
                continue;
            }
            var key = NormalizeKey(part[..separator]);
            var value = part[(separator + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0 && value.Length > 0)
            {
                result[key] = value;
            }
        }
        return result;
    }

    private static string? Get(Dictionary<string, string> declarations, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (declarations.TryGetValue(NormalizeKey(key), out var value))
            {
                return value;
            }
        }
        return null;
    }

    private static string NormalizeKey(string key) =>
        string.Concat(
            key.Where(character =>
                !char.IsWhiteSpace(character)
                && character != '-'
                && character != '_'))
            .ToLowerInvariant();

    private static Brush ParseBrush(string value, Color fallback)
    {
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.Transparent;
        }
        try
        {
            return (Brush)BrushConverter.ConvertFromString(value)!;
        }
        catch (Exception)
        {
            return new SolidColorBrush(fallback);
        }
    }

    private static double ParseDouble(string? text, double fallback) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

    private static FontWeight ParseFontWeight(string? value, string legacyCode)
    {
        var normalized = NormalizeStyleValue(value);
        return normalized switch
        {
            "thin" => FontWeights.Thin,
            "light" => FontWeights.Light,
            "medium" => FontWeights.Medium,
            "semibold" or "demibold" => FontWeights.SemiBold,
            "bold" => FontWeights.Bold,
            "extrabold" => FontWeights.ExtraBold,
            "black" => FontWeights.Black,
            "normal" or "regular" => FontWeights.Normal,
            _ when legacyCode.Contains("bold", StringComparison.OrdinalIgnoreCase) => FontWeights.Bold,
            _ => FontWeights.Normal,
        };
    }

    private static FontStyle ParseFontStyle(string? value, string legacyCode)
    {
        var normalized = NormalizeStyleValue(value);
        return normalized switch
        {
            "italic" => FontStyles.Italic,
            "oblique" => FontStyles.Oblique,
            "normal" => FontStyles.Normal,
            _ when legacyCode.Contains("italic", StringComparison.OrdinalIgnoreCase) => FontStyles.Italic,
            _ => FontStyles.Normal,
        };
    }

    private static TextAlignment ParseTextAlignment(string? value) =>
        NormalizeStyleValue(value) switch
        {
            "left" => TextAlignment.Left,
            "right" => TextAlignment.Right,
            "justify" => TextAlignment.Justify,
            _ => TextAlignment.Center,
        };

    private static CornerRadius ParseCornerRadius(string? value)
    {
        if (value is null)
        {
            return new CornerRadius(0);
        }
        var parts = value
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => ParseDouble(part, 0))
            .ToArray();
        return parts.Length switch
        {
            1 => new CornerRadius(Math.Max(0, parts[0])),
            4 => new CornerRadius(
                Math.Max(0, parts[0]),
                Math.Max(0, parts[1]),
                Math.Max(0, parts[2]),
                Math.Max(0, parts[3])),
            _ => new CornerRadius(0),
        };
    }

    private static string NormalizeStyleValue(string? value) =>
        value is null
            ? string.Empty
            : NormalizeKey(value);
}
