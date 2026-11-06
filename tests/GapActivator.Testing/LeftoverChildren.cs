using GapActivator.Application.Messaging;
using GapActivator.Application.Queries;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace GapActivator.Testing;

/// <summary>
/// The safety net of the end-to-end hosts: handles to the children of the active runs, taken before a host stops, so that a
/// child the stop left running can be found afterwards, killed and reported. Test support only.
/// </summary>
public sealed class LeftoverChildren
{
    private static readonly TimeSpan ChildExitWait = TimeSpan.FromSeconds(10);

    private readonly List<DiagnosticsProcess> _children;

    private LeftoverChildren(List<DiagnosticsProcess> children) => _children = children;

    /// <summary>
    /// Opens the processes of the active runs. Only a process whose name is in <paramref name="childProcessNames"/> is taken, so a
    /// reused PID never points at an unrelated process. Best-effort: a failing query yields none.
    /// </summary>
    public static async Task<LeftoverChildren> OpenAsync(IQueryDispatcher queries, IReadOnlyCollection<string> childProcessNames)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(childProcessNames);

        var children = new List<DiagnosticsProcess>();
        IReadOnlyList<ProcessSummary> summaries;
        try
        {
            summaries = await queries.QueryAsync<ListProcesses, IReadOnlyList<ProcessSummary>>(
                new ListProcesses(), Caller.System, CancellationToken.None);
        }
        catch (Exception)
        {
            // The stop still runs; the safety net is best-effort.
            return new LeftoverChildren(children);
        }

        foreach (int pid in summaries.Select(s => s.ActiveRun?.Pid).OfType<int>())
        {
            try
            {
                DiagnosticsProcess child = DiagnosticsProcess.GetProcessById(pid);
                if (childProcessNames.Contains(child.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    children.Add(child);
                }
                else
                {
                    child.Dispose();
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // Already gone.
            }
        }

        return new LeftoverChildren(children);
    }

    /// <summary>Kills every opened child that is still running and returns the PIDs it had to kill; releases the handles.</summary>
    public List<int> KillAndReport()
    {
        var leftovers = new List<int>();
        foreach (DiagnosticsProcess child in _children)
        {
            using (child)
            {
                if (child.HasExited)
                {
                    continue;
                }

                leftovers.Add(child.Id);
                child.Kill(entireProcessTree: true);
                child.WaitForExit(ChildExitWait);
            }
        }

        _children.Clear();
        return leftovers;
    }
}
