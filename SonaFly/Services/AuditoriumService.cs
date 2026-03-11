using Microsoft.AspNetCore.SignalR.Client;
using SonaFly.Models;

namespace SonaFly.Services;

/// <summary>
/// Manages the SignalR connection to the Auditorium hub.
/// Provides methods to join/leave rooms, play/stop/queue tracks, and events for state changes.
/// </summary>
public class AuditoriumService : IAsyncDisposable
{
    private readonly SonaFlyApiClient _api;
    private HubConnection? _hub;
    private Guid? _currentAuditoriumId;

    public event Action<AuditoriumStateDto>? StateChanged;
    public event Action<AuditoriumStateDto>? TrackStarted;
    public event Action<AuditoriumStateDto>? TrackEnded;
    public event Action<List<QueueItemDto>>? QueueUpdated;
    public event Action<string, List<ActiveUserDto>>? UserJoined;
    public event Action<string, List<ActiveUserDto>>? UserLeft;
    public event Action<string>? ErrorOccurred;

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;
    public Guid? CurrentAuditoriumId => _currentAuditoriumId;

    public AuditoriumService(SonaFlyApiClient api)
    {
        _api = api;
    }

    public async Task<AuditoriumStateDto?> JoinAsync(Guid auditoriumId)
    {
        try
        {
            await EnsureConnectedAsync();
            var state = await _hub!.InvokeAsync<AuditoriumStateDto>("JoinAuditorium", auditoriumId);
            _currentAuditoriumId = auditoriumId;
            return state;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
            return null;
        }
    }

    public async Task LeaveAsync()
    {
        try
        {
            if (_hub != null && IsConnected)
                await _hub.InvokeAsync("LeaveAuditorium");
            _currentAuditoriumId = null;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    public async Task PlayTrackAsync(Guid trackId)
    {
        try
        {
            if (_hub != null && IsConnected)
                await _hub.InvokeAsync("PlayTrack", trackId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    public async Task StopTrackAsync()
    {
        try
        {
            if (_hub != null && IsConnected)
                await _hub.InvokeAsync("StopTrack");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    public async Task SkipTrackAsync()
    {
        try
        {
            if (_hub != null && IsConnected)
                await _hub.InvokeAsync("SkipTrack");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    public async Task QueueTrackAsync(Guid trackId)
    {
        try
        {
            if (_hub != null && IsConnected)
                await _hub.InvokeAsync("QueueTrack", trackId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    public async Task RemoveFromQueueAsync(Guid queueItemId)
    {
        try
        {
            if (_hub != null && IsConnected)
                await _hub.InvokeAsync("RemoveFromQueue", queueItemId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (_hub != null && _hub.State == HubConnectionState.Connected) return;

        var baseUrl = _api.BaseUrl?.TrimEnd('/') ?? "";
        var token = _api.AccessToken ?? "";

        _hub = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/auditorium", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .WithAutomaticReconnect()
            .Build();

        // Wire up events
        _hub.On<AuditoriumStateDto>("OnTrackStarted", state =>
            MainThread.BeginInvokeOnMainThread(() => TrackStarted?.Invoke(state)));

        _hub.On<AuditoriumStateDto>("OnTrackEnded", state =>
            MainThread.BeginInvokeOnMainThread(() => TrackEnded?.Invoke(state)));

        _hub.On<List<QueueItemDto>>("OnQueueUpdated", queue =>
            MainThread.BeginInvokeOnMainThread(() => QueueUpdated?.Invoke(queue)));

        _hub.On<string, List<ActiveUserDto>>("OnUserJoined", (name, users) =>
            MainThread.BeginInvokeOnMainThread(() => UserJoined?.Invoke(name, users)));

        _hub.On<string, List<ActiveUserDto>>("OnUserLeft", (name, users) =>
            MainThread.BeginInvokeOnMainThread(() => UserLeft?.Invoke(name, users)));

        _hub.Reconnected += async (connectionId) =>
        {
            if (_currentAuditoriumId.HasValue)
            {
                var state = await _hub.InvokeAsync<AuditoriumStateDto>("JoinAuditorium", _currentAuditoriumId.Value);
                MainThread.BeginInvokeOnMainThread(() => StateChanged?.Invoke(state));
            }
        };

        await _hub.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }
}
