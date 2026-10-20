namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>
/// Порт real-time аудиотранспорта: скрывает конкретную WebRTC-библиотеку (SIPSorcery,
/// libwebrtc и т.п.) от Application. Реализация — в Infrastructure; порт не знает о pairing,
/// NSD и Android. Контракт проверяется общим набором contract tests для всех реализаций.
/// </summary>
/// <remarks>
/// <see cref="TransportEndpoint"/> — только адрес, не identity peer. Доверенная identity
/// (fingerprint, protocol version), параметры signaling и DTLS-контекст передаются адаптеру
/// composition root-ом из pairing/security подсистемы (ADR-0007) и не являются частью endpoint;
/// доверять peer по IP/mDNS запрещено AGENTS.md.
/// </remarks>
public interface IRealtimeTransport
{
    /// <summary>
    /// Устанавливает transport-соединение с peer. Ожидаемый отказ (недоступность endpoint,
    /// отказ signaling, отсутствие media path) возвращается как типизированная ошибка, а не исключение.
    /// </summary>
    /// <param name="endpoint">Endpoint peer, полученный из discovery/diagnostics.</param>
    /// <param name="cancellationToken">Отмена попытки соединения.</param>
    /// <returns>Успешная сессия либо типизированная ошибка.</returns>
    /// <exception cref="OperationCanceledException">Попытка соединения отменена.</exception>
    Task<Results.Result<IRealtimeTransportSession>> ConnectAsync(
        TransportEndpoint endpoint,
        CancellationToken cancellationToken);
}
