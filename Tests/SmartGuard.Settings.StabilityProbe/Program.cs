using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

return await ProbeController.RunAsync(args);

internal static class ProbeController
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan CleanupGrace = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = ProbeOptions.Parse(args);
            if (Directory.Exists(options.RunDirectory))
            {
                throw new ArgumentException($"Run directory already exists: {options.RunDirectory}");
            }

            Directory.CreateDirectory(options.RunDirectory);
            var result = new ProbeResult
            {
                Mode = options.Mode,
                Iteration = options.Iteration,
                Seed = options.Seed,
                Filter = options.Filter,
                ControllerPid = Environment.ProcessId,
                StartUtc = DateTimeOffset.UtcNow,
                TrxPath = options.TrxPath ?? string.Empty,
                StdoutPath = Path.Combine(options.RunDirectory, "stdout.log"),
                StderrPath = Path.Combine(options.RunDirectory, "stderr.log"),
                TestOrderPath = Path.Combine(options.RunDirectory, "test-order.json"),
            };
            WriteTestOrderManifest(result, options.SelectionPath);

            if (options.Mode == "production-equivalent")
            {
                result.Status = "Blocked";
                result.ExecutionStatus = "Blocked";
                result.ReproductionStatus = "Blocked";
                result.BlockedReason =
                    "production-equivalent is blocked until a WPF selection can prove hooks and artificial worker blocking are disabled.";
                result.TrxStatus = "NotRun";
                result.EndUtc = DateTimeOffset.UtcNow;
                WriteResult(options.RunDirectory, result);
                return 0;
            }

            await ExecuteAsync(options, result);
            WriteTestOrderManifest(result, options.SelectionPath);
            WriteResult(options.RunDirectory, result);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task ExecuteAsync(ProbeOptions options, ProbeResult result)
    {
        var command = options.TestCommand;
        if (options.TestCommandFile is not null)
        {
            command = File.ReadAllText(options.TestCommandFile, Utf8NoBom);
        }
        command = string.IsNullOrWhiteSpace(command)
            ? throw new InvalidOperationException("Test command is required.")
            : command;
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = options.WorkingDirectory,
        };
        // cmd.exe owns the command-line grammar here. Passing the complete
        // `/d /c <command>` string through ProcessStartInfo.Arguments avoids
        // a second ArgumentList encoding layer that changes quoted logger and
        // results-directory arguments before cmd.exe parses them.
        startInfo.Arguments = $"/d /c {command}";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start the selected dotnet test command.");
        }

        result.TestPid = process.Id;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!process.HasExited)
        {
            result.TimedOut = true;
            // The timeout owner is the controller, but leave one bounded
            // handoff window for the parent to capture the live testhost
            // before this controller terminates the exact process tree.
            await Task.Delay(CleanupGrace);
            try
            {
                StopExactProcessTree(process.Id);
            }
            catch (Exception exception)
            {
                result.CleanupError = exception.Message;
            }

            if (!process.WaitForExit((int)CleanupGrace.TotalMilliseconds))
            {
                result.CleanupTimedOut = true;
                result.CleanupError ??=
                    $"Timed-out child process {process.Id} did not exit within {CleanupGrace.TotalSeconds:0}s cleanup grace.";
            }
        }

        var outputDrain = Task.WhenAll(stdoutTask, stderrTask);
        if (await Task.WhenAny(outputDrain, Task.Delay(CleanupGrace)) != outputDrain)
        {
            result.CleanupTimedOut = true;
            result.CleanupError ??=
                $"Timed-out child output did not drain within {CleanupGrace.TotalSeconds:0}s cleanup grace.";
        }
        else
        {
            await File.WriteAllTextAsync(result.StdoutPath, await stdoutTask, Utf8NoBom);
            await File.WriteAllTextAsync(result.StderrPath, await stderrTask, Utf8NoBom);
        }

        if (process.HasExited)
        {
            result.ExitCode = process.ExitCode;
        }
        result.TrxStatus = ValidateTrx(options.TrxPath, out var totalCount);
        result.TotalCount = totalCount;
        result.Status = !result.TimedOut && !result.CleanupTimedOut && result.ExitCode == 0 && result.TrxStatus == "Passed"
            ? "Passed"
            : "Failed";
        result.ExecutionStatus = result.Status;
        result.ReproductionStatus = DetermineReproductionStatus(result);
        result.EndUtc = DateTimeOffset.UtcNow;
    }

    private static string DetermineReproductionStatus(ProbeResult result)
    {
        if (result.Mode == "production-equivalent")
        {
            return "Blocked";
        }
        if (result.Status == "Passed")
        {
            return "NotReproduced";
        }
        if (result.TimedOut)
        {
            return "Blocked";
        }

        var output = string.Join(
            Environment.NewLine,
            File.Exists(result.StdoutPath) ? File.ReadAllText(result.StdoutPath, Utf8NoBom) : string.Empty,
            File.Exists(result.StderrPath) ? File.ReadAllText(result.StderrPath, Utf8NoBom) : string.Empty);
        return output.Contains("paged-queued", StringComparison.OrdinalIgnoreCase)
            ? "Reproduced"
            : "Blocked";
    }

    private static void StopExactProcessTree(int processId)
    {
        using var taskkill = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "taskkill.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/PID", processId.ToString(), "/T", "/F" },
        });
        if (taskkill is null)
        {
            throw new InvalidOperationException($"Unable to terminate timed out process tree rooted at PID {processId}.");
        }

        if (!taskkill.WaitForExit((int)CleanupGrace.TotalMilliseconds))
        {
            try
            {
                taskkill.Kill(entireProcessTree: true);
            }
            catch
            {
                // The bounded failure below is the authoritative cleanup receipt.
            }
            throw new InvalidOperationException(
                $"taskkill did not exit within {CleanupGrace.TotalSeconds:0}s for process tree rooted at PID {processId}.");
        }
        if (taskkill.ExitCode != 0)
        {
            // A timeout race can let the root exit between the cancellation
            // check and taskkill. Treat that specific already-dead state as a
            // successful cleanup; a still-live root remains a hard failure.
            try
            {
                using var root = Process.GetProcessById(processId);
                if (root.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new InvalidOperationException($"taskkill failed for timed out process tree rooted at PID {processId} with exit code {taskkill.ExitCode}.");
        }
    }

    private static string ValidateTrx(string? trxPath, out int totalCount)
    {
        totalCount = 0;
        if (string.IsNullOrWhiteSpace(trxPath))
        {
            return "NotRequested";
        }
        if (!File.Exists(trxPath) || new FileInfo(trxPath).Length == 0)
        {
            return "Missing";
        }

        try
        {
            var document = XDocument.Load(trxPath, LoadOptions.None);
            var summary = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "ResultSummary");
            var counters = summary?.Elements().FirstOrDefault(element => element.Name.LocalName == "Counters");
            if (summary is null || counters is null || (string?)summary.Attribute("outcome") != "Completed")
            {
                return "Malformed";
            }
            if (!int.TryParse((string?)counters.Attribute("total"), out totalCount) || totalCount <= 0)
            {
                return "Malformed";
            }
            foreach (var name in new[] { "failed", "error", "timeout", "aborted", "inconclusive", "notExecuted" })
            {
                if (!int.TryParse((string?)counters.Attribute(name), out var count) || count != 0)
                {
                    return "Failed";
                }
            }
            return "Passed";
        }
        catch (Exception)
        {
            return "Malformed";
        }
    }

    private static void WriteTestOrderManifest(ProbeResult result, string? selectionPath)
    {
        var selectionTests = ReadSelectionTests(selectionPath);
        var manifest = new TestOrderManifest
        {
            Mode = result.Mode,
            Iteration = result.Iteration,
            Seed = result.Seed,
            Filter = result.Filter,
            Status = selectionTests.Count > 0 ? "Selection" : "Unavailable",
            Tests = selectionTests,
        };

        if (!string.IsNullOrWhiteSpace(result.TrxPath) && File.Exists(result.TrxPath))
        {
            try
            {
                var document = XDocument.Load(result.TrxPath, LoadOptions.None);
                var tests = document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "UnitTestResult")
                    .Select(element => (string?)element.Attribute("testName"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select((name, index) => new TestOrderEntry { Ordinal = index + 1, Name = name! })
                    .ToList();
                if (tests.Count > 0)
                {
                    manifest.Tests = tests;
                    manifest.Status = "Captured";
                }
                else
                {
                    manifest.Status = "Malformed";
                }
            }
            catch
            {
                manifest.Status = "Malformed";
            }
        }

        File.WriteAllText(
            result.TestOrderPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            Utf8NoBom);
    }

    private static List<TestOrderEntry> ReadSelectionTests(string? selectionPath)
    {
        if (string.IsNullOrWhiteSpace(selectionPath) || !File.Exists(selectionPath))
        {
            return new List<TestOrderEntry>();
        }

        var names = File.ReadAllLines(selectionPath, Utf8NoBom)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
        return names
            .Select((name, index) => new TestOrderEntry { Ordinal = index + 1, Name = name })
            .ToList();
    }

    private static void WriteResult(string runDirectory, ProbeResult result)
    {
        var path = Path.Combine(runDirectory, "probe-result.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Utf8NoBom);
    }
}

internal sealed class ProbeOptions
{
    public required string Mode { get; init; }
    public required string RunDirectory { get; init; }
    public required int Iteration { get; init; }
    public required int Seed { get; init; }
    public required string Filter { get; init; }
    public required int TimeoutSeconds { get; init; }
    public required string WorkingDirectory { get; init; }
    public string? TrxPath { get; init; }
    public string? TestCommand { get; init; }
    public string? TestCommandFile { get; init; }
    public string? SelectionPath { get; init; }

    public static ProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Arguments must be supplied as --name value pairs.");
            }
            values[args[index][2..]] = args[index + 1];
        }

        var mode = Required(values, "mode");
        if (mode is not ("test-contamination" or "production-equivalent"))
        {
            throw new ArgumentException("Mode must be test-contamination or production-equivalent.");
        }
        var iteration = PositiveInt(values, "iteration");
        var timeoutSeconds = PositiveInt(values, "timeout-seconds");
        if (!int.TryParse(Required(values, "seed"), out var seed))
        {
            throw new ArgumentException("seed must be an integer.");
        }

        return new ProbeOptions
        {
            Mode = mode,
            RunDirectory = Path.GetFullPath(Required(values, "run-directory")),
            Iteration = iteration,
            Seed = seed,
            Filter = Required(values, "filter"),
            TimeoutSeconds = timeoutSeconds,
            WorkingDirectory = values.TryGetValue("working-directory", out var workingDirectory)
                ? Path.GetFullPath(workingDirectory)
                : Directory.GetCurrentDirectory(),
            TrxPath = values.TryGetValue("trx-path", out var trxPath) ? Path.GetFullPath(trxPath) : null,
            TestCommand = values.TryGetValue("test-command", out var testCommand) ? testCommand : null,
            TestCommandFile = values.TryGetValue("test-command-file", out var testCommandFile)
                ? Path.GetFullPath(testCommandFile)
                : null,
            SelectionPath = values.TryGetValue("selection-path", out var selectionPath)
                ? Path.GetFullPath(selectionPath)
                : null,
        };
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"--{name} is required.");

    private static int PositiveInt(IReadOnlyDictionary<string, string> values, string name) =>
        int.TryParse(Required(values, name), out var value) && value > 0
            ? value
            : throw new ArgumentException($"--{name} must be a positive integer.");
}

internal sealed class ProbeResult
{
    public string Status { get; set; } = "Failed";
    public string ExecutionStatus { get; set; } = "Failed";
    public string ReproductionStatus { get; set; } = "Blocked";
    public string Mode { get; set; } = string.Empty;
    public int Iteration { get; set; }
    public int Seed { get; set; }
    public string Filter { get; set; } = string.Empty;
    public int ControllerPid { get; set; }
    public int? TestPid { get; set; }
    public int? TestHostPid { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public int? ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public bool ControllerTimedOut { get; set; }
    public bool CleanupTimedOut { get; set; }
    public string? CleanupError { get; set; }
    public string TrxPath { get; set; } = string.Empty;
    public string StdoutPath { get; set; } = string.Empty;
    public string StderrPath { get; set; } = string.Empty;
    public string TestOrderPath { get; set; } = string.Empty;
    public string TrxStatus { get; set; } = "NotRun";
    public int TotalCount { get; set; }
    public string? BlockedReason { get; set; }
}

internal sealed class TestOrderManifest
{
    public string Status { get; set; } = "NotRun";
    public string Mode { get; set; } = string.Empty;
    public int Iteration { get; set; }
    public int Seed { get; set; }
    public string Filter { get; set; } = string.Empty;
    public List<TestOrderEntry> Tests { get; set; } = new();
}

internal sealed class TestOrderEntry
{
    public int Ordinal { get; set; }
    public string Name { get; set; } = string.Empty;
}
