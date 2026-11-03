namespace GapActivator.Application.Messaging;

/// <summary>A public command: the caller must have the right to change data.</summary>
public interface ICommand<TResult>;

/// <summary>A command that only <see cref="Caller.System"/> may send.</summary>
public interface IInternalCommand<TResult> : ICommand<TResult>;

/// <summary>Marker: the command runs without the write gate and without a commit.</summary>
public interface IUngatedCommand;
