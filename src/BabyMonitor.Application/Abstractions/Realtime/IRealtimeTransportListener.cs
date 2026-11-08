using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>
/// Порт ожидания входящего signaling-соединения (Child, ADR-0007). Adapter обязан проверить
/// pinning identity peer по trusted store до создания media-сессии; cleartext запрещён.
/// Реализация — Infrastructure (TLS + WebRTC), Application не видит библиотек.
/// </summary>
public interface IRealtimeTransportListener
{
    /// <summary>Запускает прослушивание сигнального порта для одного доверенного peer.</summary>
    /// <param name="port">TCP-порт; 0 — выбрать свободный порт ОС.</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    /// <returns>Сессия ожидания либо типизированная ошибка.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Порт вне диапазона 0..65535.</exception>
    /// <exception cref="OperationCanceledException">Запуск отменён.</exception>
    Task<Result<IRealtimeTransportListenerSession>> StartAsync(int port, CancellationToken cancellationToken);
}
