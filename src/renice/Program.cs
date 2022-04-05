using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace renice
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var options = ParseArgs(args);
            if (options.ShowedHelp)
            {
                return 0;
            }

            if (!options.NicenessSet && !options.Dummy)
            {
                options.Logger.Log("niceness not specified");
                ShowHelp();
                return 1;
            }

            if (options.ProcessIds.Count == 0 && options.ProcessMatchers.Count == 0)
            {
                options.Logger.Log("no process ids specified");
                ShowHelp();
                return 1;
            }

            if (options.Dummy)
            {
                options.Logger.Log("Running in dummy mode: will only report and take no action");
            }

            if (options.Watch)
            {
                return await Watch(options);
            }

            var result = await DistributeTheNiceness(options)
                ? 1
                : 0;

            options.Logger.AfterStatus();

            return result;
        }

        // FIXME: this parser should really never have been made
        // -> this should have been outsourced to library code
        static Options ParseArgs(string[] args)
        {
            var lastArg = null as string;
            var result = new Options();
            foreach (var s in args)
            {
                if (Switches.Help.Contains(s))
                {
                    ShowHelp();
                    result.ShowedHelp = true;
                    return result;
                }

                if (Switches.Watch.Contains(s))
                {
                    result.Watch = true;
                    continue;
                }

                if (Switches.Verbose.Contains(s))
                {
                    result.Verbose = true;
                    continue;
                }

                if (Switches.Dummy.Contains(s))
                {
                    result.Dummy = true;
                    continue;
                }

                if (Switches.TakesArguments.Contains(s))
                {
                    lastArg = s;
                    continue;
                }

                if (Switches.Match.Contains(lastArg))
                {
                    result.ProcessMatchers.Add(s);
                    continue;
                }

                if (Switches.LogFile.Contains(lastArg))
                {
                    result.LogFile = s;
                    continue;
                }

                if (Switches.Interval.Contains(lastArg))
                {
                    result.WatchIntervalSeconds = TryParseInt(lastArg, s);
                    continue;
                }


                if (Switches.Nice.Contains(lastArg))
                {
                    result.Niceness = SymMap.TryGetValue(s, out var sym)
                        ? sym
                        : TryParseInt(lastArg, s);
                    result.NicenessSet = true;
                    continue;
                }

                result.ProcessIds.Add(TryParseInt(lastArg, s));
            }

            return result;
        }

        private static async Task<int> Watch(Options options)
        {
            var remindedUser = false;
            var interval = options.WatchIntervalSeconds * 1000;
            while (true)
            {
                if (!remindedUser)
                {
                    options.Logger.Log(
                        $"Watching processes... will {(options.Dummy ? "report" : "renice")} every {options.WatchIntervalSeconds}s... Ctrl-C to stop!");
                    remindedUser = true;
                }

                if (!await DistributeTheNiceness(options))
                {
                    options.Logger.Log("Unable to renice one or more processes -- perhaps they're dead, Jim?");
                    return 1;
                }

                Thread.Sleep(interval);
            }
        }

        private static void ShowHelp()
        {
            var thisApp = new Uri(typeof(Program).Assembly.Location).LocalPath;
            var shorter = Path.GetFileNameWithoutExtension(thisApp);
            Console.WriteLine($"usage: {shorter} {{-w}} {{-v}} -n [niceness] -p [pid] ...[pid] ...");
            Console.WriteLine("  where niceness is similar to *nix niceness");
            WriteHelpPart(Switches.Dummy, "run in dummy mode (report only, no priority alteration)");
            WriteHelpPart(Switches.Help, "show this help");
            WriteHelpPart(Switches.Interval, $"interval, in seconds, between watch rounds (default: {Options.DEFAULT_WATCH_INTERVAL_SECONDS})");
            WriteHelpPart(Switches.LogFile, "write logs to the specified file");
            WriteHelpPart(Switches.Match, "match processes by name");
            WriteHelpPart(Switches.Nice, "set the niceness from -19 (realtime) to 19 (idle)");
            WriteHelpPart(Switches.Pid, "one or more process ids to renice");
            WriteHelpPart(Switches.Verbose, "see more verbose logging");
            WriteHelpPart(Switches.Watch, "watch the processes and renice  periodically");
        }

        private static void WriteHelpPart(string[] switches, string description)
        {
            var pre = string.Join(", ", switches).PadRight(15);
            Console.WriteLine($"  {pre}  {description}");
        }

        private static async Task<bool> DistributeTheNiceness(
            Options options
        )
        {
            var selected = PriorityMap.TryGetValue(options.Niceness, out var priorityClass)
                ? priorityClass
                : throw new ArgumentException(
                    $"Unable to set priority to {options.Niceness}. Try a value from 19 (lowest) to -19");

            var error = false;
            var ids = await options.ResolveAllProcessIds();
            if (ids.Length == 0)
            {
                options.Logger.Log($"No matching process ids found");
            }

            foreach (var id in ids)
            {
                error |= TrySetPriority(id, selected, options);
            }

            return error;
        }

        private static Dictionary<int, ProcessPriorityClass> LastPriorities = new();

        private static bool TrySetPriority(
            int id,
            ProcessPriorityClass selected,
            Options options
        )
        {
            try
            {
                using var p = ProcessQuery.GetProcessById(id);
                var originalClass = p.PriorityClass;
                if (options.Dummy)
                {
                    if (LastPriorities.TryGetValue(id, out var last) &&
                        last != originalClass)
                    {
                        options.Logger.Status(
                            $"Process #{id} ({p.ProcessName}) has changed priority: {last} -> {originalClass}");
                        // leave up on-screen
                        options.Logger.AfterStatus();
                    }
                    else
                    {
                        options.Logger.Status($"Process #{id} ({p.ProcessName}) has priority: {originalClass}");
                    }

                    LastPriorities[id] = originalClass;

                    return true;
                }

                if (originalClass == selected)
                {
                    if (options.Verbose)
                    {
                        options.Logger.Status($"pid [{id}] already has priority {selected}");
                    }

                    return true;
                }

                p.PriorityClass = selected;
                if (options.Verbose)
                {
                    options.Logger.Status($"altered priority of pid [{id}] from {originalClass} to {selected}");
                    options.Logger.AfterStatus();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Can't set priority on {id}: {ex.Message}");
                return false;
            }
        }

        private static readonly Dictionary<ProcessPriorityClass, int[]> ReverseMap
            = new Dictionary<ProcessPriorityClass, int[]>()
            {
                [ProcessPriorityClass.RealTime] = new[] { -19 },
                [ProcessPriorityClass.High] = Range(-18, -9),
                [ProcessPriorityClass.AboveNormal] = Range(-10, -1),
                [ProcessPriorityClass.Normal] = new[] { 0 },
                [ProcessPriorityClass.BelowNormal] = Range(1, 10),
                [ProcessPriorityClass.Idle] = Range(10, 20)
            };

        private static readonly Dictionary<int, ProcessPriorityClass> PriorityMap
            = GeneratePriorityMap();

        private static Dictionary<int, ProcessPriorityClass> GeneratePriorityMap()
        {
            var map = new Dictionary<int, ProcessPriorityClass>();
            foreach (var kvp in ReverseMap)
            {
                foreach (var level in kvp.Value)
                {
                    map[level] = kvp.Key;
                }
            }

            return map;
        }

        private static readonly Dictionary<string, int> SymMap =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["realtime"] = -19,
                ["real-time"] = -19,
                ["rt"] = -19,
                ["high"] = -10,
                ["abovenormal"] = -8,
                ["above-normal"] = -8,
                ["above"] = -8,
                ["normal"] = 0,
                ["belownormal"] = 8,
                ["below-normal"] = 8,
                ["below"] = 8,
                ["idle"] = 19
            };

        private static int[] Range(int min, int max)
        {
            var howMany = max - min;
            var result = new int[howMany];
            var idx = 0;
            for (var i = min;
                 i < max;
                 i++)
            {
                result[idx++] = i;
            }

            return result;
        }

        private static int TryParseInt(string name, string s)
        {
            return int.TryParse(s, out var result)
                ? result
                : throw new ArgumentException($"{name} requires an integer argument (got {s})");
        }
    }

    public class Logger
    {
        private readonly Options _options;

        public Logger(Options options)
        {
            _options = options;
        }

        private int _lastMessageLength = 0;

        public void Status(string message)
        {
            var overwrite = new String(' ', _lastMessageLength);
            var timestamped = $"[{TimeStamp}] {message}";
            Console.Out.Write($"\r{overwrite}\r{timestamped}");
            _lastMessageLength = timestamped.Length;
            LogToFile(timestamped);
        }

        public void VerboseLog(string message)
        {
            if (!_options.Verbose)
            {
                return;
            }

            Log(message);
        }

        public void Log(string message)
        {
            var timestamped = $"[{TimeStamp}] {message}";
            Console.WriteLine(timestamped);
            LogToFile(timestamped);
        }

        public void VerboseStatus(string message)
        {
            LogToFile(message); // always log to file when enabled
            if (!_options.Verbose)
            {
                return;
            }

            Status(message);
        }

        public void AfterStatus()
        {
            if (_lastMessageLength > 0)
            {
                _lastMessageLength = 0;
                Console.WriteLine("");
            }
        }

        private void LogToFile(string message)
        {
            if (_options.LogFile is null)
            {
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(_options.LogFile);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllLines(_options.LogFile, new[] { message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to log to log file: {ex.Message}");
            }
        }

        private string TimeStamp
        {
            get
            {
                var now = DateTime.Now;
                return $"{now.Year:0000}-{now.Month:00}-{now.Day:00} {now.Hour:00}:{now.Minute:00}:{now.Second:00}";
            }
        }
    }

    internal static class StringExtensions
    {
        internal static string QuoteIfNecessary(
            this string s
        )
        {
            return s?.Contains(" ") ?? false
                ? $"\"{s}\""
                : $"{s}";
        }
    }
}