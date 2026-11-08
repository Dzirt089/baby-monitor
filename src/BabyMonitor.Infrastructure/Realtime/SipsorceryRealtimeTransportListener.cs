using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Realtime;

/// <summary>
/// Слушатель входящей media-сессии (Child) на SIPSorcery за портом
/// <see cref="IRealtimeTransportListener"/>: TCP + TLS signaling с mutual authentication и
/// identity assertion, проверка доверия peer до SDP, WebRTC audio-only Opus/DTLS-SRTP.
/// Недоверенный peer отклоняется внутри адаптера типизированной ошибкой и не получает сессию.
/// </summary>
public sealed class SipsorceryRealtimeTransportListener : IRealtimeTransportListener
{
    private readonly SignalingIdentity _identity;
    private readonly SipsorceryTransportOptions _options;

    /// <summary>Создаёт слушатель media-сессии.</summary>
    /// <param name="identity">Локальная identity, trusted store и ожидаемый fingerprint peer.</param>
    /// <param name="options">Настройки signaling/media; по умолчанию стандартные.</param>
    /// <exception cref="ArgumentNullException">Аргумент равен <see langword="null"/>.</exception>
    public SipsorceryRealtimeTransportListener(
        SignalingIdentity identity,
        SipsorceryTransportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
        _options = options ?? new SipsorceryTransportOptions();
    }

    /// <inheritdoc />
    public async Task<Result<IRealtimeTransportListenerSession>> StartAsync(
        int port,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var signalingServer = new TlsSignalingServer(_identity, _options.Signaling);
        var serverSessionResult = await signalingServer.StartAsync(port, cancellationToken).ConfigureAwait(false);
        if (!serverSessionResult.IsSuccess)
        {
            return Result<IRealtimeTransportListenerSession>.Failure(serverSessionResult.Error!);
        }

        var listenerSession = new SipsorceryRealtimeTransportListenerSession(serverSessionResult.Value, _options);
        return Result<IRealtimeTransportListenerSession>.Success(listenerSession);
    }
}
