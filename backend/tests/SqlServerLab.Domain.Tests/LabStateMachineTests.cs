using SqlServerLab.Domain.Labs;

namespace SqlServerLab.Domain.Tests;

public class LabStateMachineTests
{
    // Independent statement of the specification; the implementation table must match it exactly.
    private static readonly Dictionary<LabState, LabState[]> Allowed = new()
    {
        [LabState.Requested] = [LabState.Provisioning, LabState.Deleting, LabState.Failed],
        [LabState.Provisioning] = [LabState.Configuring, LabState.Failed],
        [LabState.Configuring] = [LabState.Ready, LabState.Failed],
        [LabState.Ready] = [LabState.Deallocating, LabState.Maintenance, LabState.Deleting, LabState.Failed],
        [LabState.Deallocating] = [LabState.Stopped, LabState.Failed],
        [LabState.Stopped] = [LabState.Starting, LabState.Deleting, LabState.Failed],
        [LabState.Starting] = [LabState.Configuring, LabState.Ready, LabState.Failed],
        [LabState.Maintenance] = [LabState.Ready, LabState.Failed],
        [LabState.Deleting] = [LabState.Deleted, LabState.Failed],
        [LabState.Deleted] = [],
        [LabState.Failed] = [LabState.Deleting],
    };

    public static TheoryData<LabState, LabState> AllPairs()
    {
        var data = new TheoryData<LabState, LabState>();
        foreach (var from in Enum.GetValues<LabState>())
        {
            foreach (var to in Enum.GetValues<LabState>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_transition_matches_the_specification(LabState from, LabState to)
    {
        var expected = Allowed[from].Contains(to);
        Assert.Equal(expected, LabStateMachine.CanTransition(from, to));

        var lab = NewLab(from);
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        if (expected)
        {
            LabStateMachine.Transition(lab, to, "reason", now);
            Assert.Equal(to, lab.State);
            Assert.Equal("reason", lab.StateReason);
            Assert.Equal(now, lab.UpdatedAt);
        }
        else
        {
            var ex = Assert.Throws<InvalidLabTransitionException>(() => LabStateMachine.Transition(lab, to, null, now));
            Assert.Equal(from, ex.From);
            Assert.Equal(from, lab.State);
        }
    }

    [Fact]
    public void Every_state_is_covered_by_the_table()
    {
        foreach (var state in Enum.GetValues<LabState>())
        {
            Assert.NotNull(LabStateMachine.NextStates(state));
            Assert.NotNull(LabStateMachine.AllowedCommands(state));
        }
    }

    [Fact]
    public void Deleted_is_terminal_and_cannot_be_reconciled()
    {
        Assert.True(LabStateMachine.IsTerminal(LabState.Deleted));
        Assert.Empty(LabStateMachine.NextStates(LabState.Deleted));
        Assert.Throws<InvalidLabTransitionException>(() =>
            LabStateMachine.Reconcile(NewLab(LabState.Deleted), LabState.Ready, "x", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Reconcile_adopts_observed_state_outside_the_transition_table()
    {
        var lab = NewLab(LabState.Starting);
        LabStateMachine.Reconcile(lab, LabState.Stopped, "observed", DateTimeOffset.UnixEpoch);
        Assert.Equal(LabState.Stopped, lab.State);
    }

    [Theory]
    [InlineData(LabState.Ready, new[] { LabCommand.Deallocate, LabCommand.Delete, LabCommand.ExtendExpiration })]
    [InlineData(LabState.Stopped, new[] { LabCommand.Start, LabCommand.Delete, LabCommand.ExtendExpiration })]
    [InlineData(LabState.Failed, new[] { LabCommand.Delete })]
    [InlineData(LabState.Provisioning, new[] { LabCommand.ExtendExpiration })]
    [InlineData(LabState.Deleting, new LabCommand[0])]
    [InlineData(LabState.Deleted, new LabCommand[0])]
    public void Allowed_commands_follow_state(LabState state, LabCommand[] expected) =>
        Assert.Equal(expected.Order(), LabStateMachine.AllowedCommands(state).Order());

    [Fact]
    public void Commands_only_lead_to_permitted_transitions()
    {
        foreach (var state in Enum.GetValues<LabState>())
        {
            if (LabStateMachine.IsAllowed(state, LabCommand.Start))
            {
                Assert.True(LabStateMachine.CanTransition(state, LabState.Starting));
            }

            if (LabStateMachine.IsAllowed(state, LabCommand.Deallocate))
            {
                Assert.True(LabStateMachine.CanTransition(state, LabState.Deallocating));
            }

            if (LabStateMachine.IsAllowed(state, LabCommand.Delete))
            {
                Assert.True(LabStateMachine.CanTransition(state, LabState.Deleting));
            }
        }
    }

    [Theory]
    [InlineData(LabState.Ready, VmPowerState.Running)]
    [InlineData(LabState.Stopped, VmPowerState.Deallocated)]
    [InlineData(LabState.Provisioning, VmPowerState.NotCreated)]
    [InlineData(LabState.Failed, VmPowerState.Unknown)]
    public void Implied_power_state(LabState state, VmPowerState expected) =>
        Assert.Equal(expected, LabStateMachine.ImpliedPowerState(state));

    private static Lab NewLab(LabState state) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = "u",
        Name = "lab",
        Region = "eastus",
        ResourceGroupName = "rg",
        State = state,
    };
}
