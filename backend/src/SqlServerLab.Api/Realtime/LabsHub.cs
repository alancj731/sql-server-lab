using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SqlServerLab.Api.Auth;
using SqlServerLab.Application.Abstractions;
using SqlServerLab.Application.Labs;

namespace SqlServerLab.Api.Realtime;

/// <summary>
/// Push channel for lab and job changes. Clients join their user group automatically and subscribe to individual
/// labs only after an ownership check. Server → client events: <c>labChanged</c> (LabDto), <c>jobChanged</c> (JobDto).
/// </summary>
[Authorize(Policy = LabPolicies.User)]
public sealed class LabsHub(LabService labs, ICurrentUser user) : Hub
{
    public static string UserGroup(string userId) => $"user:{userId}";
    public static string LabGroup(Guid labId) => $"lab:{labId:N}";
    public const string AdminGroup = "admins";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.UserId));
        if (user.IsAdmin)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
        }

        await base.OnConnectedAsync();
    }

    public async Task SubscribeLab(Guid labId)
    {
        if (!await labs.CanAccessAsync(labId, Context.ConnectionAborted))
        {
            throw new HubException("Lab not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, LabGroup(labId));
    }

    public Task UnsubscribeLab(Guid labId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, LabGroup(labId));
}
