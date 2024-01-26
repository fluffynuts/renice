using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace renice;

public static class Switches
{
    public static string[] Help = { "--help", "-h" };
    public static string[] Watch = { "--watch", "-w" };
    public static string[] Verbose = { "--verbose", "-v" };
    public static string[] Dummy = { "--dummy", "-d" };
    public static string[] Match = { "--match", "-m" };
    public static string[] Interval = { "--interval", "-i" };
    public static string[] LogFile = { "--logfile", "-l" };
    public static string[] Nice = { "--nice", "-n" };
    public static string[] Pid = { "--pid", "-p" };

    public static string[] TakesArguments =
        Match.Concat(Match)
            .Concat(LogFile)
            .Concat(Nice)
            .Concat(Interval)
            .Concat(Pid)
            .ToArray();
    
    public static string[] AllSwitches =
        Help.Concat(Watch)
            .Concat(Verbose)
            .Concat(Dummy)
            .Concat(Match)
            .Concat(Interval)
            .Concat(LogFile)
            .Concat(Nice)
            .Concat(Pid)
            .ToArray();
}

public class Options
{
    public const int DEFAULT_WATCH_INTERVAL_SECONDS = 5;
    public bool NicenessSet { get; set; } = false;
    public int Niceness { get; set; }
    public bool Watch { get; set; } = false;
    public int WatchIntervalSeconds { get; set; } = DEFAULT_WATCH_INTERVAL_SECONDS;

    public async Task<int[]> ResolveAllProcessIds()
    {
        return ProcessIds.Concat(
            await TryFoundMatchedProcesses()
        ).ToArray();
    }

    public List<int> ProcessIds { get; } = new List<int>();
    public bool ShowedHelp { get; set; } = false;
    public bool Verbose { get; set; } = false;
    public string LogFile { get; set; } = null;
    public bool Dummy { get; set; } = false;
    public List<string> ProcessMatchers { get; } = new List<string>();

    public Logger Logger
        => _logger ??= new Logger(this);

    private Logger _logger;

    private async Task<IEnumerable<int>> TryFoundMatchedProcesses()
    {
        if (!ProcessMatchers.Any())
        {
            return ProcessIds;
        }

        var result = new List<int>();
        var allProcesses = ProcessQuery.GetAllProcesses();
        foreach (var s in ProcessMatchers)
        {
            result.AddRange(
                await TryFindProcessesMatching(s, allProcesses)
            );
        }

        return result;
    }

    private static int MyPid => _myPid ??= Process.GetCurrentProcess().Id;
    private static int? _myPid;

    private ConcurrentDictionary<int, string> ProcessCommandlineCache = new();

    private async Task<IEnumerable<int>> TryFindProcessesMatching(
        string s,
        Process[] allProcesses
    )
    {
        Logger.VerboseLog($"Attempting to match processes with '{s}'");
        var re = new Regex(s, RegexOptions.IgnoreCase);
        var tasks = allProcesses
            .Where(p => p.Id != MyPid)
            .Select(p =>
            {
                return Task.Run(() =>
                {
                    try
                    {
                        if (!ProcessCommandlineCache.TryGetValue(p.Id, out var commandLine))
                        {
                            commandLine = TryGet(() => ProcessCommandLine.Retrieve(p, out var result) == 0
                                ? result
                                : ""
                            );
                            ProcessCommandlineCache.TryAdd(p.Id, commandLine);
                        }

                        var strings = new[]
                        {
                            p.MainWindowTitle,
                            p.ProcessName,
                            commandLine
                        };
                        return new
                        {
                            p.Id,
                            p.MainWindowTitle,
                            p.ProcessName,
                            CommandLine = commandLine,
                            IsMatch = strings.Any(re.IsMatch)
                        };
                    }
                    catch
                    {
                        return null;
                    }
                });
            });
        var results = await Task.WhenAll(tasks);
        return results.Where(o => o?.IsMatch == true)
            .Select(o =>
            {
                Logger.VerboseLog(string.IsNullOrWhiteSpace(o.MainWindowTitle)
                    ? $"match: {o.CommandLine}"
                    : $"match: {o.CommandLine} ({o.MainWindowTitle})");
                return o.Id;
            })
            .ToArray();

        string TryGet(Func<string> func)
        {
            try
            {
                return func();
            }
            catch
            {
                return "";
            }
        }
    }
}