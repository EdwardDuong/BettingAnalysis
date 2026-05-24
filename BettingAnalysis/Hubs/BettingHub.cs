using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BettingAnalysis.Hubs;

/// <summary>
/// SignalR hub for real-time odds and opportunity updates.
/// Clients can subscribe to a specific sport or receive all updates.
///
/// Client events emitted:
///   OddsRefreshed   — fired by OddsRefreshService after each background refresh
///   BankrollUpdated — bankroll state after a bet result is recorded
/// </summary>
[Authorize]
public class BettingHub : Hub
{
    private readonly ILogger<BettingHub> _logger;

    public BettingHub(ILogger<BettingHub> logger) => _logger = logger;

    /// <summary>Subscribe to odds updates for a specific sport (e.g. "EPL", "AFL").</summary>
    public async Task SubscribeToSport(string sport)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"sport-{sport}");
        _logger.LogDebug("Client {ConnectionId} subscribed to {Sport}", Context.ConnectionId, sport);
        await Clients.Caller.SendAsync("Subscribed", sport);
    }

    /// <summary>Unsubscribe from a sport group.</summary>
    public async Task UnsubscribeFromSport(string sport)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"sport-{sport}");
        await Clients.Caller.SendAsync("Unsubscribed", sport);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
