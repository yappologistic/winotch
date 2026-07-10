using System.Globalization;
using System.Text.RegularExpressions;

namespace Winotch.CommandBar;

public sealed class UnitConverterProvider : ICommandProvider
{
    public string Name => "Unit Converter";
    public int Priority => 60;

    public bool IsEnabled(CommandBarSettings settings) => settings.UnitConverterEnabled;

    public Task<IReadOnlyList<CommandBarResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        if (!UnitConverter.TryConvert(query, out var conversion))
        {
            return Task.FromResult<IReadOnlyList<CommandBarResult>>([]);
        }

        return Task.FromResult<IReadOnlyList<CommandBarResult>>([
            new CommandBarResult(
                conversion.ResultText,
                $"Unit conversion · {conversion.InputText}",
                Name,
                94,
                Priority,
                _ => ClipboardWriter.WriteTextAsync(conversion.ResultText),
                null,
                "\uE9D2")
        ]);
    }
}

public sealed record UnitConversion(string InputText, string ResultText);

public static class UnitConverter
{
    private sealed record Unit(string Dimension, double Factor, double Offset = 0);

    private static readonly Regex QueryPattern = new(
        @"^\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*(?<from>[a-zA-Z]+)\s+(?:to|in|as)\s+(?<to>[a-zA-Z]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<string, Unit> Units = CreateUnits();

    public static bool TryConvert(string query, out UnitConversion conversion)
    {
        conversion = new UnitConversion("", "");
        var match = QueryPattern.Match(query);
        if (!match.Success ||
            !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var fromName = match.Groups["from"].Value.ToLowerInvariant();
        var toName = match.Groups["to"].Value.ToLowerInvariant();
        if (!Units.TryGetValue(fromName, out var from) ||
            !Units.TryGetValue(toName, out var to) ||
            !StringComparer.Ordinal.Equals(from.Dimension, to.Dimension))
        {
            return false;
        }

        var result = from.Dimension == "temperature"
            ? ConvertTemperature(value, fromName, toName)
            : value * from.Factor / to.Factor;
        conversion = new UnitConversion(query, $"{Format(result)} {toName}");
        return true;
    }

    private static double ConvertTemperature(double value, string from, string to)
    {
        var celsius = from switch
        {
            "f" or "fahrenheit" => (value - 32) * 5 / 9,
            "k" or "kelvin" => value - 273.15,
            _ => value
        };
        return to switch
        {
            "f" or "fahrenheit" => celsius * 9 / 5 + 32,
            "k" or "kelvin" => celsius + 273.15,
            _ => celsius
        };
    }

    private static string Format(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static IReadOnlyDictionary<string, Unit> CreateUnits()
    {
        var units = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
        Add(units, "length", 1, "m", "meter", "meters");
        Add(units, "length", 0.01, "cm", "centimeter", "centimeters");
        Add(units, "length", 0.001, "mm", "millimeter", "millimeters");
        Add(units, "length", 1000, "km", "kilometer", "kilometers");
        Add(units, "length", 0.3048, "ft", "foot", "feet");
        Add(units, "length", 0.0254, "in", "inch", "inches");
        Add(units, "length", 1609.344, "mi", "mile", "miles");

        Add(units, "weight", 1, "g", "gram", "grams");
        Add(units, "weight", 1000, "kg", "kilogram", "kilograms");
        Add(units, "weight", 453.59237, "lb", "lbs", "pound", "pounds");
        Add(units, "weight", 28.349523125, "oz", "ounce", "ounces");

        Add(units, "data", 1, "b", "byte", "bytes");
        Add(units, "data", 1024, "kb", "kilobyte", "kilobytes");
        Add(units, "data", 1024 * 1024, "mb", "megabyte", "megabytes");
        Add(units, "data", 1024 * 1024 * 1024, "gb", "gigabyte", "gigabytes");

        Add(units, "time", 1, "s", "sec", "second", "seconds");
        Add(units, "time", 60, "min", "minute", "minutes");
        Add(units, "time", 3600, "h", "hr", "hour", "hours");
        Add(units, "time", 86400, "d", "day", "days");

        Add(units, "temperature", 1, "c", "celsius");
        Add(units, "temperature", 1, "f", "fahrenheit");
        Add(units, "temperature", 1, "k", "kelvin");
        return units;
    }

    private static void Add(Dictionary<string, Unit> units, string dimension, double factor, params string[] names)
    {
        foreach (var name in names)
        {
            units[name] = new Unit(dimension, factor);
        }
    }
}

