namespace GamePanel.Infrastructure.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// Requires the authenticated policy. SignalR hubs are anonymous by default,
/// so the attribute and endpoint policy both protect negotiate and upgrades.
/// </summary>
[Authorize(Policy = "authenticated")]
public class ServerHub : Hub
{
}