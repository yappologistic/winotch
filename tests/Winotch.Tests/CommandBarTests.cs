using Winotch.CommandBar;

namespace Winotch.Tests;

public sealed class CommandBarTests
{
    [Fact]
    public void FuzzyScoreRanksExactAboveSubsequence()
    {
        var exact = CommandMatch.Score("calculator", "Calculator");
        var subsequence = CommandMatch.Score("cltr", "Calculator");

        Assert.True(exact > subsequence);
        Assert.True(subsequence > 0);
    }

    [Theory]
    [InlineData("2 + 3 * 4", "14")]
    [InlineData("(2 + 3) * 4", "20")]
    [InlineData("2 ^ 3 ^ 2", "512")]
    [InlineData("1.5 * 2", "3")]
    [InlineData("-2 + 5", "3")]
    public void CalculatorEvaluatesMathExpressions(string expression, string expected)
    {
        var evaluated = CalculatorEvaluator.TryEvaluate(expression, out var value, out var error);

        Assert.True(evaluated, error);
        Assert.Equal(expected, CalculatorEvaluator.Format(value));
    }

    [Theory]
    [InlineData("System.IO.File.Delete('x')")]
    [InlineData("1 + alert(1)")]
    [InlineData("1; 2")]
    [InlineData("hello")]
    public void CalculatorRejectsNonMathInput(string expression)
    {
        var evaluated = CalculatorEvaluator.TryEvaluate(expression, out _, out _);

        Assert.False(evaluated);
    }

    [Fact]
    public void CalculatorRejectsDivisionByZero()
    {
        var evaluated = CalculatorEvaluator.TryEvaluate("10 / 0", out _, out var error);

        Assert.False(evaluated);
        Assert.Equal("Division by zero.", error);
    }

    [Theory]
    [InlineData("1 m to cm", "100 cm")]
    [InlineData("32 f to c", "0 c")]
    [InlineData("1 gb to mb", "1024 mb")]
    [InlineData("2 h to min", "120 min")]
    public void UnitConverterConvertsLocalUnits(string query, string expected)
    {
        var converted = UnitConverter.TryConvert(query, out var conversion);

        Assert.True(converted);
        Assert.Equal(expected, conversion.ResultText);
    }

    [Fact]
    public async Task CommandBarServiceRanksProviderResults()
    {
        var service = new CommandBarService(
            [
                new FakeProvider("Low", 10, new CommandBarResult("Calendar", "", "Low", 50, 10, CommandBarResult.Noop)),
                new FakeProvider("High", 100, new CommandBarResult("Calculator", "", "High", 80, 100, CommandBarResult.Noop))
            ],
            () => new CommandBarSettings());

        var results = await service.QueryAsync("calc");

        Assert.Equal("Calculator", results[0].Title);
    }

    [Fact]
    public async Task AppLaunchProviderFindsWindowsCalculatorWhenShortcutIsMissing()
    {
        var provider = new AppLaunchProvider();

        var results = await provider.QueryAsync("calc", CancellationToken.None);

        Assert.Contains(results, result => result.Title == "Calculator");
    }

    [Fact]
    public void HotkeyParserParsesDefaultHotkey()
    {
        var parsed = CommandHotkeyParser.TryParse("Ctrl+Alt+Space", out var hotkey);

        Assert.True(parsed);
        Assert.Equal(CommandHotkey.ModControl | CommandHotkey.ModAlt, hotkey.Modifiers);
        Assert.Equal(0x20u, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Ctrl+Ctrl+Space")]
    [InlineData("Ctrl+Alt+")]
    [InlineData("Ctrl+Unknown")]
    public void HotkeyParserRejectsInvalidStrings(string text)
    {
        var parsed = CommandHotkeyParser.TryParse(text, out _);

        Assert.False(parsed);
    }

    private sealed class FakeProvider : ICommandProvider
    {
        private readonly CommandBarResult _result;

        public FakeProvider(string name, int priority, CommandBarResult result)
        {
            Name = name;
            Priority = priority;
            _result = result;
        }

        public string Name { get; }
        public int Priority { get; }

        public bool IsEnabled(CommandBarSettings settings) => true;

        public Task<IReadOnlyList<CommandBarResult>> QueryAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CommandBarResult>>([_result]);
    }
}
