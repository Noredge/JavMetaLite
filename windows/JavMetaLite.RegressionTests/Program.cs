using System.Diagnostics;

namespace JavMetaLite.RegressionTests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.FirstOrDefault() == "--save-performance")
            return await SavePerformance.RunAsync(args.Skip(1).ToArray());
        IReadOnlyList<RegressionTestCase> tests = FileOrganizationRegressionTests.All
            .Concat(OutputDownloadRegressionTests.All).Concat(FileSharingRegressionTests.All)
            .Concat(LocalSampleSafetyRegressionTests.All).Concat(FolderSidecarRegressionTests.All)
            .Concat(TemporaryCleanupRegressionTests.All).Concat(ConfirmedPartsRegressionTests.All).ToArray();
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

        Console.WriteLine($"JavMetaLite file transaction regression suite — {tests.Count} test(s)");
        var suiteTimer = Stopwatch.StartNew();
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var timer = Stopwatch.StartNew();
            Task? execution = null;
            Console.WriteLine($"START [{test.Category}] {test.Name}");
            try
            {
                execution = test.Run();
                await execution.WaitAsync(TimeSpan.FromMinutes(2));
                Console.WriteLine($"PASS  [{test.Category}] {test.Name} ({timer.ElapsedMilliseconds} ms)");
            }
            catch (TimeoutException) when (execution is { IsCompleted: false })
            {
                Console.Error.WriteLine($"REGRESSION TIMEOUT [{test.Category}] {test.Name}: exceeded 2 minutes; remaining cases were not run.");
                // Do not continue other filesystem tests beside a hung fixture.
                // Terminate the test process even if a broken fixture owns a foreground thread.
                Environment.Exit(1);
                return 1;
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
