using System.Diagnostics;

namespace JavMetaLite.RegressionTests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var tests = FileOrganizationRegressionTests.All;
        if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
        {
            foreach (var test in tests)
            {
                Console.WriteLine($"{test.Category,-12} {test.Name}");
            }
            return 0;
        }

        var category = ReadOption(args, "--category");
        if (!string.IsNullOrWhiteSpace(category))
        {
            tests = tests
                .Where(test => test.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (tests.Count == 0)
            {
                Console.Error.WriteLine($"Unknown category: {category}");
                return 2;
            }
        }

        Console.WriteLine($"JavMetaLite v0.4 regression suite — {tests.Count} test(s)");
        var suiteTimer = Stopwatch.StartNew();
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                await test.Run();
                Console.WriteLine($"PASS  [{test.Category}] {test.Name} ({timer.ElapsedMilliseconds} ms)");
            }
            catch (Exception exception)
            {
                failures.Add($"[{test.Category}] {test.Name}: {exception.Message}");
                Console.WriteLine($"FAIL  [{test.Category}] {test.Name} ({timer.ElapsedMilliseconds} ms)");
                Console.WriteLine($"      {exception}");
            }
        }

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine($"REGRESSION PASS  {tests.Count}/{tests.Count} ({suiteTimer.Elapsed.TotalSeconds:F2} s)");
            return 0;
        }

        Console.WriteLine($"REGRESSION FAIL  {failures.Count}/{tests.Count} failed ({suiteTimer.Elapsed.TotalSeconds:F2} s)");
        foreach (var failure in failures)
        {
            Console.WriteLine($"- {failure}");
        }
        return 1;
    }

    private static string? ReadOption(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }
        return null;
    }
}
