using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TSLib;
using TSLib.Full;
using TSLib.Helper;
using TSLib.Messages;
using TSLib.Scheduler;

namespace TS_DJ.TeamSpeak;

/// <summary>
/// TeamSpeak connection service. All TsFullClient operations run on the
/// DedicatedTaskScheduler thread via InvokeAsync.
/// </summary>
public sealed class TeamSpeakService : ITeamSpeakService, IDisposable
{
    private readonly ILogger<TeamSpeakService> _logger;
    private readonly IdentityStore _identityStore;
    private readonly TeamSpeakOptions _options;
    private readonly DedicatedTaskScheduler _scheduler;
    private readonly TsFullClient _client;
    private readonly Ts3VoiceTarget _voiceTarget;
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _closed;

    public TeamSpeakService(
        ILogger<TeamSpeakService> logger,
        IdentityStore identityStore,
        TeamSpeakOptions? options = null)
    {
        _logger = logger;
        _identityStore = identityStore;
        _options = options ?? new TeamSpeakOptions();

        var id = new Id(1);
        _scheduler = new DedicatedTaskScheduler(id);
        _client = new TsFullClient(_scheduler);
        _voiceTarget = new Ts3VoiceTarget(_client, logger);
        _client.OnDisconnected += OnClientDisconnected;
    }

    public ConnectionState State => _state;

    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<string>? StatusMessage;

    public Ts3VoiceTarget VoiceTarget => _voiceTarget;

    public DedicatedTaskScheduler Scheduler => _scheduler;

    public TsFullClient Client => _client;

    public async Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        SetState(ConnectionState.Connecting);
        RaiseStatus("Connecting…");
        _logger.LogInformation("Connecting to {Address} as {Nickname}", settings.Address, settings.Nickname);

        try
        {
            var identity = await ResolveIdentityAsync(cancellationToken);
            var address = NormalizeAddress(settings.Address);

            var connectionConfig = new ConnectionDataFull(
                address,
                identity,
                username: settings.Nickname,
                serverPassword: string.IsNullOrEmpty(settings.ServerPassword)
                    ? Password.Empty
                    : Password.FromPlain(settings.ServerPassword),
                defaultChannel: settings.Channel);

            var connectResult = await _scheduler.InvokeAsync(async () =>
            {
                _closed = false;
                return await _client.Connect(connectionConfig);
            });

            if (!connectResult.GetOk(out var error))
            {
                _logger.LogError("Could not connect: {Error}", error.ErrorFormat());
                SetState(ConnectionState.Error);
                RaiseStatus($"Connection failed: {error.Message}");
                return;
            }

            var self = _client.Book.Self();
            _logger.LogInformation(
                "Connected as {Nickname} in channel {Channel}",
                settings.Nickname,
                self?.Channel.ToString() ?? settings.Channel);
            SetState(ConnectionState.Connected);
            RaiseStatus($"Connected — channel {self?.Channel.ToString() ?? settings.Channel}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect failed");
            SetState(ConnectionState.Error);
            RaiseStatus($"Connection error: {ex.Message}");
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _closed = true;
        _logger.LogInformation("Disconnecting…");
        RaiseStatus("Disconnecting…");

        await _scheduler.InvokeAsync(_client.Disconnect);

        SetState(ConnectionState.Disconnected);
        RaiseStatus("Disconnected");
        _logger.LogInformation("Disconnected");
    }

    public async Task SetNicknameAsync(string nickname, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            throw new ArgumentException("Nickname must not be empty.", nameof(nickname));

        if (_state != ConnectionState.Connected)
        {
            _logger.LogDebug("SetNickname ignored — not connected");
            return;
        }

        await _scheduler.InvokeAsync(() => _client.ChangeName(nickname));
        _logger.LogDebug("Nickname changed to {Nickname}", nickname);
    }

    private void OnClientDisconnected(object? sender, DisconnectEventArgs e)
    {
        if (e.Error != null)
            _logger.LogWarning("TeamSpeak disconnected with error: {Error}", e.Error.ErrorFormat());
        else
            _logger.LogInformation("TeamSpeak disconnected. Reason: {Reason}", e.ExitReason);

        SetState(ConnectionState.Disconnected);
        RaiseStatus(_closed ? "Disconnected" : $"Disconnected ({e.ExitReason})");
    }

    private async Task<IdentityData> ResolveIdentityAsync(CancellationToken cancellationToken)
    {
        var (key, offset) = await _identityStore.LoadAsync(cancellationToken);

        IdentityData identity;
        if (string.IsNullOrEmpty(key))
        {
            identity = TsCrypt.GenerateNewIdentity(_options.SecurityLevel);
            _logger.LogInformation(
                "Created new TeamSpeak identity (security level {Level})",
                _options.SecurityLevel);
            await _identityStore.SaveAsync(
                identity.PrivateKeyString,
                (int)identity.ValidKeyOffset,
                cancellationToken);
        }
        else
        {
            var identityResult = TsCrypt.LoadIdentityDynamic(key, (ulong)offset);
            if (!identityResult.Ok)
            {
                _logger.LogError("Failed to load identity: {Error}", identityResult.Error);
                throw new InvalidOperationException($"Corrupted identity: {identityResult.Error}");
            }

            identity = identityResult.Value;
            _logger.LogInformation("Loaded TeamSpeak identity (offset {Offset})", identity.ValidKeyOffset);
        }

        if (TsCrypt.GetSecurityLevel(identity) < _options.SecurityLevel)
        {
            _logger.LogInformation(
                "Improving identity security to level {Level}",
                _options.SecurityLevel);
            TsCrypt.ImproveSecurity(identity, _options.SecurityLevel);
            await _identityStore.SaveAsync(
                identity.PrivateKeyString,
                (int)identity.ValidKeyOffset,
                cancellationToken);
        }

        return identity;
    }

    private static string NormalizeAddress(string address)
    {
        address = address.Trim();
        if (string.IsNullOrEmpty(address))
            throw new ArgumentException("Server address is required.", nameof(address));

        if (address.StartsWith('['))
        {
            if (address.Contains("]:"))
                return address;

            return address + ":9987";
        }

        if (!address.Contains(':'))
            return address + ":9987";

        return address;
    }

    private void SetState(ConnectionState state)
    {
        if (_state == state)
            return;

        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void RaiseStatus(string message) => StatusMessage?.Invoke(this, message);

    public void Dispose()
    {
        _client.OnDisconnected -= OnClientDisconnected;

        if (_state == ConnectionState.Connected && !_closed)
        {
            try
            {
                _scheduler.InvokeAsync(_client.Disconnect).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Best-effort disconnect during dispose failed");
            }
        }

        _client.Dispose();
        _scheduler.Dispose();
    }
}
