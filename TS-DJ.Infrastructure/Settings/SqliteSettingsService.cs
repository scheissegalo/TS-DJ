using System.Data.Common;
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

            _logger.LogDebug("Loaded audio settings from {DatabasePath}", _databasePath);
            return new AudioSettings { OpusBitrateKbps = kbps };
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
