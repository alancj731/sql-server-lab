using System.Collections.Frozen;

namespace SqlServerLab.Domain.Labs;

/// <summary>
/// Pure lab lifecycle rules. Every state change made by the worker goes through <see cref="Transition"/>;
/// reconciliation uses <see cref="Reconcile"/> to adopt the observed infrastructure state.
/// </summary>
public static class LabStateMachine
{
    private static readonly FrozenDictionary<LabState, FrozenSet<LabState>> Transitions =
        new Dictionary<LabState, FrozenSet<LabState>>
        {
            [LabState.Requested] = Set(LabState.Provisioning, LabState.Deleting, LabState.Failed),
            [LabState.Provisioning] = Set(LabState.Configuring, LabState.Failed),
            [LabState.Configuring] = Set(LabState.Ready, LabState.Failed),
            [LabState.Ready] = Set(LabState.Deallocating, LabState.Maintenance, LabState.Deleting, LabState.Failed),
            [LabState.Deallocating] = Set(LabState.Stopped, LabState.Failed),
            [LabState.Stopped] = Set(LabState.Starting, LabState.Deleting, LabState.Failed),
            [LabState.Starting] = Set(LabState.Configuring, LabState.Ready, LabState.Failed),
            [LabState.Maintenance] = Set(LabState.Ready, LabState.Failed),
            [LabState.Deleting] = Set(LabState.Deleted, LabState.Failed),
            [LabState.Deleted] = Set(),
            [LabState.Failed] = Set(LabState.Deleting),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<LabState, FrozenSet<LabCommand>> Commands =
        new Dictionary<LabState, FrozenSet<LabCommand>>
        {
            [LabState.Requested] = CommandSet(LabCommand.ExtendExpiration),
            [LabState.Provisioning] = CommandSet(LabCommand.ExtendExpiration),
            [LabState.Configuring] = CommandSet(LabCommand.ExtendExpiration),
            [LabState.Ready] = CommandSet(LabCommand.Deallocate, LabCommand.Delete, LabCommand.ExtendExpiration),
            [LabState.Deallocating] = CommandSet(LabCommand.ExtendExpiration),
            [LabState.Stopped] = CommandSet(LabCommand.Start, LabCommand.Delete, LabCommand.ExtendExpiration),
            [LabState.Starting] = CommandSet(LabCommand.ExtendExpiration),
            [LabState.Maintenance] = CommandSet(LabCommand.ExtendExpiration),
            [LabState.Deleting] = CommandSet(),
            [LabState.Deleted] = CommandSet(),
            [LabState.Failed] = CommandSet(LabCommand.Delete),
        }.ToFrozenDictionary();

    public static bool CanTransition(LabState from, LabState to) => Transitions[from].Contains(to);

    public static IReadOnlySet<LabState> NextStates(LabState from) => Transitions[from];

    public static void Transition(Lab lab, LabState to, string? reason, DateTimeOffset now)
    {
        if (!CanTransition(lab.State, to))
        {
            throw new InvalidLabTransitionException(lab.State, to);
        }

        lab.State = to;
        lab.StateReason = reason;
        lab.UpdatedAt = now;
    }

    /// <summary>
    /// Adopt a state observed during reconciliation. Bypasses the transition table on purpose, but never
    /// resurrects a deleted lab.
    /// </summary>
    public static void Reconcile(Lab lab, LabState observed, string reason, DateTimeOffset now)
    {
        if (lab.State == LabState.Deleted)
        {
            throw new InvalidLabTransitionException(lab.State, observed);
        }

        lab.State = observed;
        lab.StateReason = reason;
        lab.UpdatedAt = now;
    }

    public static bool IsAllowed(LabState state, LabCommand command) => Commands[state].Contains(command);

    public static IReadOnlySet<LabCommand> AllowedCommands(LabState state) => Commands[state];

    public static bool IsTerminal(LabState state) => state == LabState.Deleted;

    /// <summary>Best-effort power state implied by the control-plane state.</summary>
    public static VmPowerState ImpliedPowerState(LabState state) => state switch
    {
        LabState.Requested or LabState.Provisioning => VmPowerState.NotCreated,
        LabState.Starting => VmPowerState.Starting,
        LabState.Configuring or LabState.Ready or LabState.Maintenance => VmPowerState.Running,
        LabState.Deallocating => VmPowerState.Deallocating,
        LabState.Stopped => VmPowerState.Deallocated,
        LabState.Deleted => VmPowerState.Deleted,
        _ => VmPowerState.Unknown,
    };

    private static FrozenSet<LabState> Set(params LabState[] states) => states.ToFrozenSet();

    private static FrozenSet<LabCommand> CommandSet(params LabCommand[] commands) => commands.ToFrozenSet();
}
