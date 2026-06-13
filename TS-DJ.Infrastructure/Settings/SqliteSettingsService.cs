using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Settings;

public sealed class SqliteSettingsService : ISettingsService, IDisposable
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly ILogger<SqliteSettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqliteSettingsService(string databasePath, ILogger<SqliteSettingsService> logger)
    {
        _databasePath = databasePath;
        _logger = logger;

        var directory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException($"Invalid settings database path: {databasePath}");
        Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS app_settings (
                    key TEXT PRIMARY KEY NOT NULL,
                    value TEXT
                );
                """;
            command.ExecuteNonQuery();

            _logger.LogInformation("Settings database ready at {DatabasePath}", _databasePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize settings database at {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not initialize settings database at '{_databasePath}'.", ex);
        }
    }

    public async Task<ConnectionSettings> LoadConnectionSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);

            var settings = new ConnectionSettings
            {
                Address = await ReadSettingAsync(connection, "connection.address", cancellationToken) ?? string.Empty,
                Nickname = await ReadSettingAsync(connection, "connection.nickname", cancellationToken) ?? "TS-DJ",
                ServerPassword = await ReadSettingAsync(connection, "connection.server_password", cancellationToken) ?? string.Empty,
                Channel = await ReadSettingAsync(connection, "connection.channel", cancellationToken) ?? string.Empty,
                IdentityPrivateKey = await ReadSettingAsync(connection, "identity.private_key", cancellationToken) ?? string.Empty,
                IdentityOffset = int.TryParse(
                    await ReadSettingAsync(connection, "identity.offset", cancellationToken),
                    out var offset) ? offset : 0,
                SecurityLevel = int.TryParse(
                    await ReadSettingAsync(connection, "identity.security_level", cancellationToken),
                    out var level) ? level : 8
            };

            _logger.LogDebug("Loaded connection settings from {DatabasePath}", _databasePath);
            return settings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load connection settings from {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not load connection settings from '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveConnectionSettingsAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await WriteSettingAsync(connection, transaction, "connection.address", settings.Address, cancellationToken);
            await WriteSettingAsync(connection, transaction, "connection.nickname", settings.Nickname, cancellationToken);
            await WriteSettingAsync(connection, transaction, "connection.server_password", settings.ServerPassword, cancellationToken);
            await WriteSettingAsync(connection, transaction, "connection.channel", settings.Channel, cancellationToken);
            await WriteSettingAsync(connection, transaction, "identity.private_key", settings.IdentityPrivateKey, cancellationToken);
            await WriteSettingAsync(connection, transaction, "identity.offset", settings.IdentityOffset.ToString(), cancellationToken);
            await WriteSettingAsync(connection, transaction, "identity.security_level", settings.SecurityLevel.ToString(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Saved connection settings to {DatabasePath}", _databasePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to save connection settings to {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not save connection settings to '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AudioSettings> LoadAudioSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var raw = await ReadSettingAsync(connection, AudioSettings.OpusBitrateKbpsKey, cancellationToken);
            var kbps = int.TryParse(raw, out var parsed)
                ? TS_DJ.Core.Audio.OpusBitratePresets.Normalize(parsed)
                : TS_DJ.Core.Audio.OpusBitratePresets.Default;

            var masterRaw = await ReadSettingAsync(connection, AudioSettings.MasterVolumeKey, cancellationToken);
            var musicRaw = await ReadSettingAsync(connection, AudioSettings.MusicVolumeKey, cancellationToken);

            _logger.LogDebug("Loaded audio settings from {DatabasePath}", _databasePath);
            return new AudioSettings
            {
                OpusBitrateKbps = kbps,
                MasterVolumeHuman = ClampVolumeHuman(masterRaw, 50),
                MusicVolumeHuman = ClampVolumeHuman(musicRaw, 50)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load audio settings from {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not load audio settings from '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAudioSettingsAsync(AudioSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var kbps = TS_DJ.Core.Audio.OpusBitratePresets.Normalize(settings.OpusBitrateKbps);
            await WriteSettingAsync(connection, transaction, AudioSettings.OpusBitrateKbpsKey, kbps.ToString(), cancellationToken);
            await WriteSettingAsync(connection, transaction, AudioSettings.MasterVolumeKey,
                ClampVolumeHuman(settings.MasterVolumeHuman, 50).ToString(), cancellationToken);
            await WriteSettingAsync(connection, transaction, AudioSettings.MusicVolumeKey,
                ClampVolumeHuman(settings.MusicVolumeHuman, 50).ToString(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Saved audio settings to {DatabasePath}", _databasePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to save audio settings to {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not save audio settings to '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SoundboardSettings> LoadSoundboardSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var raw = await ReadSettingAsync(connection, SoundboardSettings.ConfigKey, cancellationToken);

            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogDebug("No soundboard settings found — using defaults");
                return new SoundboardSettings();
            }

            var settings = JsonSerializer.Deserialize<SoundboardSettings>(raw);
            if (settings is null || settings.Pads.Count != SoundboardSettings.PadCount)
            {
                _logger.LogWarning("Invalid soundboard settings — using defaults");
                return new SoundboardSettings();
            }

            _logger.LogDebug("Loaded soundboard settings from {DatabasePath}", _databasePath);
            return settings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load soundboard settings from {DatabasePath}", _databasePath);
            return new SoundboardSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSoundboardSettingsAsync(SoundboardSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var json = JsonSerializer.Serialize(settings);
            await WriteSettingAsync(connection, transaction, SoundboardSettings.ConfigKey, json, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Saved soundboard settings to {DatabasePath}", _databasePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to save soundboard settings to {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not save soundboard settings to '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UiSettings> LoadUiSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);

            var settings = new UiSettings
            {
                Width = ParseNullableDouble(await ReadSettingAsync(connection, UiSettings.WidthKey, cancellationToken)),
                Height = ParseNullableDouble(await ReadSettingAsync(connection, UiSettings.HeightKey, cancellationToken)),
                X = ParseNullableDouble(await ReadSettingAsync(connection, UiSettings.XKey, cancellationToken)),
                Y = ParseNullableDouble(await ReadSettingAsync(connection, UiSettings.YKey, cancellationToken)),
                WindowState = await ReadSettingAsync(connection, UiSettings.WindowStateKey, cancellationToken) ?? "Normal",
                IsSoundboardVisible = ParseNullableBool(
                    await ReadSettingAsync(connection, UiSettings.SoundboardVisibleKey, cancellationToken), defaultValue: true)
            };

            _logger.LogDebug("Loaded UI settings from {DatabasePath}", _databasePath);
            return settings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load UI settings from {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not load UI settings from '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveUiSettingsAsync(UiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await WriteSettingAsync(connection, transaction, UiSettings.WidthKey,
                settings.Width?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, cancellationToken);
            await WriteSettingAsync(connection, transaction, UiSettings.HeightKey,
                settings.Height?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, cancellationToken);
            await WriteSettingAsync(connection, transaction, UiSettings.XKey,
                settings.X?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, cancellationToken);
            await WriteSettingAsync(connection, transaction, UiSettings.YKey,
                settings.Y?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, cancellationToken);
            await WriteSettingAsync(connection, transaction, UiSettings.WindowStateKey, settings.WindowState, cancellationToken);
            await WriteSettingAsync(connection, transaction, UiSettings.SoundboardVisibleKey,
                settings.IsSoundboardVisible.ToString(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Saved UI settings to {DatabasePath}", _databasePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to save UI settings to {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not save UI settings to '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<NavidromeSettings> LoadNavidromeSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var raw = await ReadSettingAsync(connection, NavidromeSettings.ConfigKey, cancellationToken);

            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogDebug("No Navidrome settings found — using defaults");
                return new NavidromeSettings();
            }

            var settings = JsonSerializer.Deserialize<NavidromeSettings>(raw);
            if (settings is null)
            {
                _logger.LogWarning("Invalid Navidrome settings — using defaults");
                return new NavidromeSettings();
            }

            _logger.LogDebug("Loaded Navidrome settings from {DatabasePath}", _databasePath);
            return settings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load Navidrome settings from {DatabasePath}", _databasePath);
            return new NavidromeSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveNavidromeSettingsAsync(NavidromeSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var json = JsonSerializer.Serialize(settings);
            await WriteSettingAsync(connection, transaction, NavidromeSettings.ConfigKey, json, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Saved Navidrome settings to {DatabasePath}", _databasePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to save Navidrome settings to {DatabasePath}", _databasePath);
            throw new InvalidOperationException(
                $"Could not save Navidrome settings to '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static int ClampVolumeHuman(string? raw, int defaultValue)
    {
        if (!int.TryParse(raw, out var value))
            return defaultValue;

        return Math.Clamp(value, 0, 100);
    }

    private static int ClampVolumeHuman(int value, int defaultValue)
    {
        if (value is < 0 or > 100)
            return defaultValue;

        return value;
    }

    private static double? ParseNullableDouble(string? raw) =>
        double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool ParseNullableBool(string? raw, bool defaultValue) =>
        bool.TryParse(raw, out var value) ? value : defaultValue;

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Setting key must not be empty.", nameof(key));

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var value = await ReadSettingAsync(connection, key, cancellationToken);
            _logger.LogTrace("Read setting {Key} from {DatabasePath}", key, _databasePath);
            return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to read setting {Key} from {DatabasePath}", key, _databasePath);
            throw new InvalidOperationException(
                $"Could not read setting '{key}' from '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetSettingAsync(string? key, string? value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
            return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await WriteSettingAsync(connection, transaction, key, value ?? string.Empty, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogTrace("Wrote setting {Key} to {DatabasePath}", key, _databasePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to write setting {Key} to {DatabasePath}", key, _databasePath);
            throw new InvalidOperationException(
                $"Could not write setting '{key}' to '{_databasePath}'.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<string?> ReadSettingAsync(
        SqliteConnection connection,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static async Task WriteSettingAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO app_settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public void Dispose() => _lock.Dispose();
}
