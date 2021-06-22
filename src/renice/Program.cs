using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
                if (s == "--help" || s == "-h")
                {
                    ShowHelp();
                    return result;
                }

                if (s == "-w")
                {
                    result.Watch = true;
                    continue;
                }

                if (s == "-v")
                {
                    result.Verbose = true;
                    continue;
                }

                if (s == "-d")
                {
                    result.Dummy = true;
                    continue;
                }

                if (s == "-n" || s == "-p" || s == "-l" || s == "-f")
                {
                    lastArg = s;
                    continue;
                }

                if (lastArg == "-f")
                {
                    result.ProcessMatchers.Add(s);
                    continue;
                }

                if (lastArg == "-l")
                {
                    result.LogFile = s;
                    continue;
                }


                if (lastArg == "-n")
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
            while (true)
            {
                if (!remindedUser)
                {
                    options.Logger.Log("Watching processes... will renice every 1s... Ctrl-C to stop!");
                    remindedUser = true;
                }

                if (!await DistributeTheNiceness(options))
                {
                    options.Logger.Log($"Unable to renice one or more processes -- perhaps they're dead, Jim?");
                    return 1;
                }

                Thread.Sleep(1000);
            }
        }

        private static void ShowHelp()
        {
            var thisApp = new Uri(typeof(Program).Assembly.Location).LocalPath;
            var shorter = Path.GetFileNameWithoutExtension(thisApp);
            Console.WriteLine($"usage: {shorter} {{-w}} {{-v}} -n [niceness] -p [pid] ...[pid] ...");
            Console.WriteLine("  where niceness is similar to *nix niceness");
            Console.WriteLine("  ie: -19 is real-time and 19 is idle");
            Console.WriteLine("  add -d to run in dummy mode (report only, no priority alteration)");
            Console.WriteLine("  add -l {path} to save logs to a file");
            Console.WriteLine("  add -v to see verbose logging");
            Console.WriteLine("  add -w to watch the processes and renice periodically");
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

        private static bool TrySetPriority(
            int id,
            ProcessPriorityClass selected,
            Options options
        )
        {
            try
            {
                using var p = Process.GetProcessById(id);
                var originalClass = p.PriorityClass;
                if (options.Dummy)
                {
                    options.Logger.Log($"Process #{id} ({p.ProcessName}) has priority: {originalClass}");
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

    public class Options
    {
        public bool NicenessSet { get; set; } = false;
        public int Niceness { get; set; }
        public bool Watch { get; set; } = false;

        public async Task<int[]> ResolveAllProcessIds()
        {
            return ProcessIds.Concat(
                await TryFoundMatchedProcesses()
            ).ToArray();
        }

        public List<int> ProcessIds { get; } = new List<int>();
        public bool ShowedHelp { get; } = false;
        public bool Verbose { get; set; } = false;
        public string LogFile { get; set; } = null;
        public bool Dummy { get; set; } = false;
        public List<string> ProcessMatchers { get; } = new List<string>();

        public Logger Logger
            => _logger ??= new Logger(this);

        private Logger _logger;

        private async Task<IEnumerable<int>> TryFoundMatchedProcesses()
        {
            var result = new List<int>();
            foreach (var s in ProcessMatchers)
            {
                result.AddRange(
                    await TryFindProcessesMatching(s)
                );
            }

            return result;
        }
        
        private static int MyPid => _myPid ??= Process.GetCurrentProcess().Id;
        private static int? _myPid;

        private async Task<IEnumerable<int>> TryFindProcessesMatching(
            string s
        )
        {
            Logger.VerboseLog($"Attempting to match processes with '{s}'");
            var re = new Regex(s, RegexOptions.IgnoreCase);
            var tasks = Process.GetProcesses()
                .Where(p => p.Id != MyPid)
                .Select(p =>
                {
                    return Task.Run(() =>
                    {
                        try
                        {
                            var commandLine = TryGet(() => ProcessCommandLine.Retrieve(p, out var result) == 0
                                ? result
                                : ""
                            );
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
            Console.WriteLine(message);
            LogToFile(message);
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