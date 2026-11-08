using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Фабрика типизированных ошибок signaling/media. Сообщения безопасны для пользователя: не содержат
/// SDP, ключей, fingerprints, адресов и иных технических деталей.
/// </summary>
internal static class SignalingErrors
{
    internal static Error PeerUnavailable() => new(
        RealtimeTransportErrorCodes.PeerUnavailable,
        "Детское устройство недоступно в локальной сети.");

    internal static Error TlsFailure() => new(
        RealtimeTransportErrorCodes.TlsFailure,
        "Не удалось установить защищённое signaling-соединение.");

    internal static Error UnauthorizedPeer() => new(
        RealtimeTransportErrorCodes.UnauthorizedPeer,
        "Устройство не прошло проверку доверия. Выполните повторное сопряжение.");

    internal static Error SignalingTimeout() => new(
        RealtimeTransportErrorCodes.SignalingTimeout,
        "Signaling-соединение не установлено за отведённое время.");

    internal static Error UnsupportedVersion() => new(
        RealtimeTransportErrorCodes.UnsupportedVersion,
        "Версия протокола не поддерживается. Обновите приложение на обоих устройствах.");

    internal static Error MediaPathAbsent() => new(
        RealtimeTransportErrorCodes.MediaPathAbsent,
        "Signaling установлен, но аудиоканал не поднялся.");

    internal static Error MediaFlowLost() => new(
        RealtimeTransportErrorCodes.MediaFlowLost,
        "Аудиопоток прерван.");

    internal static Error NetworkUnavailable() => new(
        RealtimeTransportErrorCodes.NetworkUnavailable,
        "Сеть недоступна или изменилась.");

    internal static Error SessionClosed() => new(
        RealtimeTransportErrorCodes.SessionClosed,
        "Сессия закрыта.");

    internal static Error InvalidState() => new(
        RealtimeTransportErrorCodes.InvalidState,
        "Порядок операций нарушен: действие недоступно в текущем состоянии.");

    internal static Error Internal() => new(
        RealtimeTransportErrorCodes.TlsFailure,
        "Не удалось выполнить криптографическую операцию signaling.");

    /// <summary>Отображает код отказа из кадра error в типизированную ошибку приложения.</summary>
    internal static Error FromWireError(SignalingWireError wireError) => wireError switch
    {
        SignalingWireError.UnsupportedVersion => UnsupportedVersion(),
        SignalingWireError.UnauthorizedPeer => UnauthorizedPeer(),
        SignalingWireError.Malformed => TlsFailure(),
        _ => TlsFailure()
    };
}
