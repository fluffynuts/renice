using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace renice
{
    class Program
    {
        static int Main(string[] args)
        {
            var niceness = 0;
            var processIds = new List<int>();
            var lastArg = null as string;
            foreach (var s in args)
            {
                if (s == "-n" || s == "-p")
                {
                    lastArg = s;
                    continue;
                }

                if (lastArg == "-n")
                {
                    niceness = TryParseInt(lastArg, s);
                    continue;
                }

                processIds.Add(TryParseInt("-p", s));
            }
            
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
                : throw new ArgumentException($"Unable to set priority to {niceness}. Try a value from 19 (lowest) to -19");

            var error = false;
            foreach (var id in processIds)
            {
                error |= TrySetPriority(id, selected);
            }
            
            return error ? 1 : 0;
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