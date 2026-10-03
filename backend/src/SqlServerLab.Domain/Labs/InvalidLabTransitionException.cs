namespace SqlServerLab.Domain.Labs;

public sealed class InvalidLabTransitionException(LabState from, LabState to)
    : InvalidOperationException($"Lab cannot move from {from} to {to}.")
{
    public LabState From { get; } = from;
    public LabState To { get; } = to;
}
