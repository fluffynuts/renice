using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace renice;

public static class ProcessQuery
{
    private static Dictionary<int, Process> _processes = new();
    public static Process[] GetAllProcesses()
    {
        var result = Process.GetProcesses();
        _processes = result.ToDictionary(o => o.Id, o => o);
        return result;
    }

    public static Process GetProcessById(int id)
    {
        return _processes.TryGetValue(id, out var result)
            ? result
            : Process.GetProcessById(id);
    }
}