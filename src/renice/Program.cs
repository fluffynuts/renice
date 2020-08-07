using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace renice
{
    class Program
    {
        static int Main(string[] args)
        {
            var (niceness, watch, processIds, showedHelp) = ParseArgs(args);
            if (showedHelp)
            {
                return 0;
            }

            if (niceness is null)
            {
                Console.WriteLine("niceness not specified");
                ShowHelp();
                return 1;
            }

            if (processIds.Length == 0)
            {
                Console.WriteLine("no process ids specified");
                ShowHelp();
                return 1;
            }

            if (watch)
            {
                return Watch(niceness.Value, processIds);
            }

            return DistributeTheNiceness(niceness.Value, processIds)
                ? 1
                : 0;

        }

        static (int? niceness, bool watch, int[] processIds, bool showedHelp) ParseArgs(string[] args)
        {
            var niceness = null as int?;
            var processIds = new List<int>();
            var lastArg = null as string;
            var watch = false;
            foreach (var s in args)
            {
                if (s == "--help" || s == "-h")
                {
                    ShowHelp();
                    return (null, false, new int[0], true);
                }

                if (s == "-w")
                {
                    watch = true;
                    continue;
                }

                if (s == "-n" || s == "-p")
                {
                    lastArg = s;
                    continue;
                }

                if (lastArg == "-n")
                {
                    niceness = SymMap.TryGetValue(s, out var sym)
                        ? sym
                        : TryParseInt(lastArg, s);

                    continue;
                }

                processIds.Add(TryParseInt(lastArg, s));
            }
            return (niceness, watch, processIds.ToArray(), false);
        }

        private static int Watch(int niceness, int[] processIds)
        {
            var remindedUser = false;
            while (true)
            {
                if (!DistributeTheNiceness(niceness, processIds))
                {
                    Console.WriteLine($"Unable to renice one or more processes -- perhaps they're dead, Jim?");
                    return 1;
                }

                if (!remindedUser)
                {
                    Console.WriteLine("Watching processes... will renice every 1s... Ctrl-C to stop!");
                    remindedUser = true;
                }

                Thread.Sleep(1000);
            }
        }

        private static void ShowHelp()
        {
            var thisApp = new Uri(typeof(Program).Assembly.Location).LocalPath;
            var shorter = Path.GetFileNameWithoutExtension(thisApp);
            Console.WriteLine($"usage: {shorter} {{-w}} -n [niceness] -p [pid] ...[pid] ...");
            Console.WriteLine("  where niceness is similar to *nix niceness");
            Console.WriteLine("  ie: -19 is real-time and 19 is idle");
            Console.WriteLine("  add -w to watch the processes and renice periodically");
        }

        private static bool DistributeTheNiceness(
            int niceness, 
            int[] processIds)
        {
            var map = new Dictionary<int, ProcessPriorityClass>();
            foreach (var kvp in ReverseMap)
            {
                foreach (var level in kvp.Value)
                {
                    map[level] = kvp.Key;
                }
            }

            var selected = map.TryGetValue(niceness, out var priorityClass)
                ? priorityClass
                : throw new ArgumentException(
                    $"Unable to set priority to {niceness}. Try a value from 19 (lowest) to -19");

            var error = false;
            foreach (var id in processIds)
            {
                error |= TrySetPriority(id, selected);
            }

            return error;
        }

        private static bool TrySetPriority(int id, ProcessPriorityClass selected)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                p.PriorityClass = selected;
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
}