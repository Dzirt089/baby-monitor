using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Realtime;

/// <summary>
/// Инициатор media-сессии (Parent) на SIPSorcery за портом <see cref="IRealtimeTransport"/>
/// (ADR-0005/ADR-0007): TCP + TLS signaling с pinning identity из trusted store, обмен SDP,
/// WebRTC audio-only Opus/DTLS-SRTP, ICE host-only. Endpoint — только адрес; доверенная identity
/// peer задаётся composition root-ом через <see cref="SignalingIdentity"/>.
/// </summary>
public sealed class SipsorceryRealtimeTransport : IRealtimeTransport
{
    private readonly SignalingIdentity _identity;
    private readonly SipsorceryTransportOptions _options;

    /// <summary>Создаёт media-транспорт инициатора.</summary>
    /// <param name="identity">Локальная identity, trusted store и ожидаемый fingerprint peer.</param>
    /// <param name="options">Настройки signaling/media; по умолчанию стандартные.</param>
    /// <exception cref="ArgumentNullException">Аргумент равен <see langword="null"/>.</exception>
    public SipsorceryRealtimeTransport(SignalingIdentity identity, SipsorceryTransportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
        _options = options ?? new SipsorceryTransportOptions();
    }

    /// <inheritdoc />
    public async Task<Result<IRealtimeTransportSession>> ConnectAsync(
        TransportEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        var signalingClient = new TlsSignalingClient(endpoint, _identity, _options.Signaling);
        var signalingResult = await signalingClient.ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (!signalingResult.IsSuccess)
        {
            return Result<IRealtimeTransportSession>.Failure(signalingResult.Error!);
        }

        var signalingConnection = signalingResult.Value;
        try
        {
            var mediaResult = await SipsorceryMediaHandshake
                .CreateOfferAsync(signalingConnection, _options, cancellationToken)
                .ConfigureAwait(false);
            if (!mediaResult.IsSuccess)
            {
                await signalingConnection.DisposeAsync().ConfigureAwait(false);
                return Result<IRealtimeTransportSession>.Failure(mediaResult.Error!);
            }

            var session = new SipsorceryRealtimeTransportSession(signalingConnection, mediaResult.Value, _options);
            return Result<IRealtimeTransportSession>.Success(session);
        }
        catch
        {
            await signalingConnection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
