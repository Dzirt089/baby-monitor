using System.Net;
using BabyMonitor.Application.Results;
using BabyMonitor.Infrastructure.Signaling;
using SIPSorcery.Net;

namespace BabyMonitor.Infrastructure.Realtime;

/// <summary>
/// WebRTC media handshake SIPSorcery: создание RTCPeerConnection audio-only (Opus 48 kHz mono),
/// offer/answer через аутентифицированный signaling, сбор ICE host candidates и ожидание
/// DTLS-SRTP connected. Типы SIPSorcery не покидают Infrastructure; ошибки преобразуются
/// в типизированные Application-ошибки.
/// </summary>
internal static class SipsorceryMediaHandshake
{
    /// <summary>Динамический payload type Opus в SDP (стандартная договорённость WebRTC).</summary>
    internal const int OpusPayloadType = 111;

    /// <summary>Длительность одного Opus-кадра 20 мс в RTP-единицах 48 kHz.</summary>
    internal const uint OpusFrameRtpUnits = 960;

    private const int OpusClockRate = 48_000;
    private const int OpusChannels = 1;
    private const int PollIntervalMilliseconds = 20;

    /// <summary>Создаёт peer connection с ICE host-only (STUN/TURN вне scope MVP).</summary>
    internal static RTCPeerConnection CreatePeerConnection(SipsorceryTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configuration = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>(),
            iceTransportPolicy = RTCIceTransportPolicy.all
        };

        if (options.RtpBindAddress is not null)
        {
            configuration.X_BindAddress = options.RtpBindAddress;
        }

        return new RTCPeerConnection(configuration);
    }

    /// <summary>Добавляет audio-only Opus-медиатрек с заданным направлением (Parent — RecvOnly, Child — SendOnly).</summary>
    internal static void AddOpusMediaTrack(RTCPeerConnection peerConnection, MediaStreamStatusEnum direction)
    {
        ArgumentNullException.ThrowIfNull(peerConnection);
        var format = new SDPAudioVideoMediaFormat(
            SDPMediaTypesEnum.audio,
            OpusPayloadType,
            "opus",
            OpusClockRate,
            OpusChannels,
            fmtp: null);
        var track = new MediaStreamTrack(
            SDPMediaTypesEnum.audio,
            isRemote: false,
            new List<SDPAudioVideoMediaFormat> { format },
            direction);
        peerConnection.addTrack(track);
    }

    /// <summary>
    /// Parent (инициатор, receiver): создаёт offer recvonly, обменивается SDP через signaling,
    /// применяет answer и ждёт DTLS-SRTP connected. При успехе владение peer connection переходит
    /// вызывающему; при ошибке освобождается здесь.
    /// </summary>
    internal static async Task<Result<RTCPeerConnection>> CreateOfferAsync(
        TlsSignalingConnection signalingConnection,
        SipsorceryTransportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signalingConnection);
        ArgumentNullException.ThrowIfNull(options);

        using var mediaCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        mediaCts.CancelAfter(options.MediaConnectTimeout);
        var mediaToken = mediaCts.Token;
        RTCPeerConnection? peerConnection = null;
        var mediaPhase = false;
        try
        {
            peerConnection = CreatePeerConnection(options);
            AddOpusMediaTrack(peerConnection, MediaStreamStatusEnum.RecvOnly);

            var offerResult = await CreateLocalDescriptionAsync(peerConnection, isOffer: true, mediaToken)
                .ConfigureAwait(false);
            if (!offerResult.IsSuccess)
            {
                return Result<RTCPeerConnection>.Failure(offerResult.Error!);
            }

            await signalingConnection.SendOfferAsync(offerResult.Value, mediaToken).ConfigureAwait(false);
            var answerResult = await ReceiveAnswerAsync(signalingConnection, mediaToken).ConfigureAwait(false);
            if (!answerResult.IsSuccess)
            {
                return Result<RTCPeerConnection>.Failure(answerResult.Error!);
            }

            var setResult = peerConnection.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = answerResult.Value
            });
            if (setResult != SetDescriptionResultEnum.OK)
            {
                return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
            }

            mediaPhase = true;
            await WaitForConnectedAsync(peerConnection, mediaToken).ConfigureAwait(false);

            var connectedPeerConnection = peerConnection;
            peerConnection = null;
            return Result<RTCPeerConnection>.Success(connectedPeerConnection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result<RTCPeerConnection>.Failure(
                mediaPhase ? SignalingErrors.MediaPathAbsent() : SignalingErrors.SignalingTimeout());
        }
        catch (SignalingConnectionClosedException)
        {
            return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
        }
        catch (SignalingProtocolException)
        {
            return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
        }
        catch (IOException)
        {
            return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
        }
        finally
        {
            peerConnection?.Dispose();
        }
    }

    /// <summary>
    /// Child (отвечающая сторона, sender): принимает offer, добавляет sendonly Opus-трек, формирует
    /// answer, ждёт DTLS-SRTP connected. При успехе владение peer connection переходит вызывающему.
    /// </summary>
    internal static async Task<Result<RTCPeerConnection>> CreateAnswerAsync(
        TlsSignalingConnection signalingConnection,
        SipsorceryTransportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signalingConnection);
        ArgumentNullException.ThrowIfNull(options);

        using var mediaCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        mediaCts.CancelAfter(options.MediaConnectTimeout);
        var mediaToken = mediaCts.Token;
        RTCPeerConnection? peerConnection = null;
        var mediaPhase = false;
        try
        {
            peerConnection = CreatePeerConnection(options);
            AddOpusMediaTrack(peerConnection, MediaStreamStatusEnum.SendOnly);

            var offerResult = await ReceiveOfferAsync(signalingConnection, mediaToken).ConfigureAwait(false);
            if (!offerResult.IsSuccess)
            {
                return Result<RTCPeerConnection>.Failure(offerResult.Error!);
            }

            var setResult = peerConnection.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = offerResult.Value
            });
            if (setResult != SetDescriptionResultEnum.OK)
            {
                return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
            }

            var answerResult = await CreateLocalDescriptionAsync(peerConnection, isOffer: false, mediaToken)
                .ConfigureAwait(false);
            if (!answerResult.IsSuccess)
            {
                return Result<RTCPeerConnection>.Failure(answerResult.Error!);
            }

            await signalingConnection.SendAnswerAsync(answerResult.Value, mediaToken).ConfigureAwait(false);

            mediaPhase = true;
            await WaitForConnectedAsync(peerConnection, mediaToken).ConfigureAwait(false);

            var connectedPeerConnection = peerConnection;
            peerConnection = null;
            return Result<RTCPeerConnection>.Success(connectedPeerConnection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result<RTCPeerConnection>.Failure(
                mediaPhase ? SignalingErrors.MediaPathAbsent() : SignalingErrors.SignalingTimeout());
        }
        catch (SignalingConnectionClosedException)
        {
            return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
        }
        catch (SignalingProtocolException)
        {
            return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
        }
        catch (IOException)
        {
            return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Result<RTCPeerConnection>.Failure(SignalingErrors.MediaPathAbsent());
        }
        finally
        {
            peerConnection?.Dispose();
        }
    }

    private static async Task<Result<string>> CreateLocalDescriptionAsync(
        RTCPeerConnection peerConnection,
        bool isOffer,
        CancellationToken cancellationToken)
    {
        var description = isOffer ? peerConnection.createOffer(null) : peerConnection.createAnswer(null);
        if (description is null || string.IsNullOrWhiteSpace(description.sdp))
        {
            return Result<string>.Failure(SignalingErrors.MediaPathAbsent());
        }

        await peerConnection.setLocalDescription(description).ConfigureAwait(false);
        await WaitForIceGatheringAsync(peerConnection, cancellationToken).ConfigureAwait(false);

        var sessionDescription = peerConnection.localDescription?.sdp?.RawString();
        if (string.IsNullOrWhiteSpace(sessionDescription))
        {
            return Result<string>.Failure(SignalingErrors.MediaPathAbsent());
        }

        return Result<string>.Success(sessionDescription);
    }

    private static async Task WaitForIceGatheringAsync(RTCPeerConnection peerConnection, CancellationToken cancellationToken)
    {
        if (peerConnection.iceGatheringState == RTCIceGatheringState.complete)
        {
            return;
        }

        var gatheringCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnIceGatheringStateChanged(RTCIceGatheringState state)
        {
            if (state == RTCIceGatheringState.complete)
            {
                gatheringCompleted.TrySetResult();
            }
        }

        peerConnection.onicegatheringstatechange += OnIceGatheringStateChanged;
        try
        {
            await gatheringCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            peerConnection.onicegatheringstatechange -= OnIceGatheringStateChanged;
        }
    }

    private static async Task WaitForConnectedAsync(RTCPeerConnection peerConnection, CancellationToken cancellationToken)
    {
        // Короткий опрос вместо хрупкой подписки на события: connected и завершение DTLS могут
        // прийти разными событиями одного handshake.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (peerConnection.connectionState == RTCPeerConnectionState.connected
                && peerConnection.IsDtlsNegotiationComplete)
            {
                return;
            }

            if (peerConnection.connectionState is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
            {
                throw new InvalidOperationException("Media-соединение завершилось до установки DTLS-SRTP.");
            }

            await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<Result<string>> ReceiveOfferAsync(
        TlsSignalingConnection signalingConnection,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await signalingConnection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            switch (message.Kind)
            {
                case SignalingMessageKind.Offer:
                    return Result<string>.Success(message.SessionDescription!);
                case SignalingMessageKind.Ping:
                    await signalingConnection.SendPongAsync(message.ControlPayload, cancellationToken).ConfigureAwait(false);
                    break;
                case SignalingMessageKind.Error:
                    return Result<string>.Failure(SignalingErrors.FromWireError(message.WireError ?? SignalingWireError.Malformed));
                default:
                    return Result<string>.Failure(SignalingErrors.MediaPathAbsent());
            }
        }
    }

    private static async Task<Result<string>> ReceiveAnswerAsync(
        TlsSignalingConnection signalingConnection,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await signalingConnection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            switch (message.Kind)
            {
                case SignalingMessageKind.Answer:
                    return Result<string>.Success(message.SessionDescription!);
                case SignalingMessageKind.Ping:
                    await signalingConnection.SendPongAsync(message.ControlPayload, cancellationToken).ConfigureAwait(false);
                    break;
                case SignalingMessageKind.Error:
                    return Result<string>.Failure(SignalingErrors.FromWireError(message.WireError ?? SignalingWireError.Malformed));
                default:
                    return Result<string>.Failure(SignalingErrors.MediaPathAbsent());
            }
        }
    }
}
