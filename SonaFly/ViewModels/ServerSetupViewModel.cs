using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class ServerSetupViewModel : ObservableObject
{
    private readonly ServerStorageService _storage;
    private readonly SonaFlyApiClient _api;

    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private string _serverName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private List<ServerConfig> _servers = [];

    public ServerSetupViewModel(ServerStorageService storage, SonaFlyApiClient api)
    {
        _storage = storage;
        _api = api;
        LoadServers();
    }

    public void LoadServers() => Servers = _storage.GetAll().ToList();

    [RelayCommand]
    private async Task AddServerAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl)) return;

        IsBusy = true;
        StatusMessage = "Testing connection...";
        try
        {
            var url = ServerUrl.TrimEnd('/');
            if (!url.StartsWith("http")) url = "http://" + url;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // Try /api/genres as a lightweight test; 401 = server is alive but needs auth (that's OK)
            var resp = await client.GetAsync($"{url}/api/genres");
            if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                resp.EnsureSuccessStatusCode();

            var config = new ServerConfig
            {
                Name = string.IsNullOrWhiteSpace(ServerName) ? new Uri(url).Host : ServerName,
                BaseUrl = url
            };
            _storage.Add(config);
            LoadServers();
            ServerUrl = string.Empty;
            ServerName = string.Empty;
            StatusMessage = "Server added!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void RemoveServer(string id)
    {
        _storage.Remove(id);
        LoadServers();
    }

    [RelayCommand]
    private async Task SelectServerAsync(string id)
    {
        _storage.SetActive(id);
        if (Application.Current is App app)
            await app.NavigateToLogin();
    }
}
