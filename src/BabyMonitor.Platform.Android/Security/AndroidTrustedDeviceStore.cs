using System.Text.Json;
using Android.Content;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using AppResult = BabyMonitor.Application.Results.Result;

namespace BabyMonitor.Platform.Android.Security;

/// <summary>
/// Trusted store на SharedPreferences: только публичные данные (SPKI, fingerprint, роль, время).
/// Секреты/private keys не сохраняются. Повреждённые записи отбрасываются, JSON-ошибка → store_failed.
/// </summary>
public sealed class AndroidTrustedDeviceStore : ITrustedDeviceStore
{
    private const string PreferencesName = "babymonitor_security";
    private const string DevicesKey = "trusted_devices_v1";

    private readonly object _sync = new();
    private readonly ISharedPreferences _preferences;

    /// <summary>Создаёт store поверх private SharedPreferences приложения.</summary>
    /// <param name="context">Android context приложения.</param>
    public AndroidTrustedDeviceStore(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var applicationContext = context.ApplicationContext ?? context;
        _preferences = applicationContext.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?? throw new ArgumentException("SharedPreferences недоступны.", nameof(context));
    }

    /// <inheritdoc />
    public Task<Result<PairedDevice?>> FindAsync(string fingerprintSha256, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintSha256);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var loaded = Load();
            if (!loaded.IsSuccess)
            {
                return Task.FromResult(Result<PairedDevice?>.Failure(loaded.Error!));
            }

            var match = loaded.Value.FirstOrDefault(device =>
                string.Equals(device.PeerIdentity.FingerprintSha256, fingerprintSha256, StringComparison.Ordinal));
            return Task.FromResult(Result<PairedDevice?>.Success(match));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PairedDevice>>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var loaded = Load();
            return Task.FromResult(loaded.IsSuccess
                ? Result<IReadOnlyList<PairedDevice>>.Success(loaded.Value)
                : Result<IReadOnlyList<PairedDevice>>.Failure(loaded.Error!));
        }
    }

    /// <inheritdoc />
    public Task<AppResult> SaveAsync(PairedDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var loaded = Load();
            if (!loaded.IsSuccess)
            {
                return Task.FromResult(AppResult.Failure(loaded.Error!));
            }

            var devices = loaded.Value
                .Where(existing => !string.Equals(
                    existing.PeerIdentity.FingerprintSha256,
                    device.PeerIdentity.FingerprintSha256,
                    StringComparison.Ordinal))
                .ToList();
            devices.Add(device);
            return Task.FromResult(Persist(devices));
        }
    }

    /// <inheritdoc />
    public Task<AppResult> RemoveAsync(string fingerprintSha256, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintSha256);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var loaded = Load();
            if (!loaded.IsSuccess)
            {
                return Task.FromResult(AppResult.Failure(loaded.Error!));
            }

            var devices = loaded.Value
                .Where(device => !string.Equals(
                    device.PeerIdentity.FingerprintSha256,
                    fingerprintSha256,
                    StringComparison.Ordinal))
                .ToList();
            return Task.FromResult(Persist(devices));
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> IsTrustedAsync(string fingerprintSha256, CancellationToken cancellationToken)
    {
        var found = await FindAsync(fingerprintSha256, cancellationToken);
        return found.IsSuccess
            ? Result<bool>.Success(found.Value is not null)
            : Result<bool>.Failure(found.Error!);
    }

    private Result<List<PairedDevice>> Load()
    {
        var json = _preferences.GetString(DevicesKey, null);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<List<PairedDevice>>.Success([]);
        }

        try
        {
            var dtos = JsonSerializer.Deserialize<List<TrustedDeviceDto>>(json) ?? [];
            var devices = new List<PairedDevice>(dtos.Count);
            foreach (var dto in dtos)
            {
                if (TryCreateDevice(dto, out var device))
                {
                    devices.Add(device);
                }
            }

            return Result<List<PairedDevice>>.Success(devices);
        }
        catch (JsonException)
        {
            return Result<List<PairedDevice>>.Failure(StoreFailure());
        }
    }

    private AppResult Persist(IReadOnlyList<PairedDevice> devices)
    {
        try
        {
            var dtos = devices.Select(ToDto).ToArray();
            var json = JsonSerializer.Serialize(dtos);
            using var editor = _preferences.Edit();
            if (editor is null)
            {
                return AppResult.Failure(StoreFailure());
            }

            editor.PutString(DevicesKey, json);
            return editor.Commit()
                ? AppResult.Success()
                : AppResult.Failure(StoreFailure());
        }
        catch (Exception)
        {
            return AppResult.Failure(StoreFailure());
        }
    }

    private static TrustedDeviceDto ToDto(PairedDevice device) => new()
    {
        FingerprintSha256 = device.PeerIdentity.FingerprintSha256,
        SubjectPublicKeyInfo = Convert.ToBase64String(device.PeerIdentity.SubjectPublicKeyInfo.ToArray()),
        LocalRole = device.LocalRole.ToString(),
        PairedAtUnixSeconds = device.PairedAt.ToUnixTimeSeconds()
    };

    private static bool TryCreateDevice(TrustedDeviceDto? dto, out PairedDevice device)
    {
        device = null!;
        if (dto is null
            || string.IsNullOrWhiteSpace(dto.FingerprintSha256)
            || string.IsNullOrWhiteSpace(dto.SubjectPublicKeyInfo)
            || string.IsNullOrWhiteSpace(dto.LocalRole)
            || !Enum.TryParse<DeviceRole>(dto.LocalRole, ignoreCase: false, out var role)
            || role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            return false;
        }

        try
        {
            var spki = Convert.FromBase64String(dto.SubjectPublicKeyInfo);
            var identity = DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(spki);
            if (!string.Equals(identity.FingerprintSha256, dto.FingerprintSha256, StringComparison.Ordinal))
            {
                return false;
            }

            device = new PairedDevice(identity, role, DateTimeOffset.FromUnixTimeSeconds(dto.PairedAtUnixSeconds));
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static Error StoreFailure() =>
        new(PairingErrorCodes.StoreFailed, "Не удалось прочитать/сохранить trusted devices.");

    private sealed class TrustedDeviceDto
    {
        public string FingerprintSha256 { get; set; } = string.Empty;

        public string SubjectPublicKeyInfo { get; set; } = string.Empty;

        public string LocalRole { get; set; } = string.Empty;

        public long PairedAtUnixSeconds { get; set; }
    }
}
