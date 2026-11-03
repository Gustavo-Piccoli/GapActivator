using GapActivator.Application.Events;

namespace GapActivator.Application.Ports;

public interface IHistoryWriter
{
    /// <summary>Stages the entry; it is saved by <see cref="IUnitOfWork.CommitAsync"/>.</summary>
    void Add(HistoryEntry entry);
}
