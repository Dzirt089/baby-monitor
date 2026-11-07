namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Фаза пользовательского сценария сопряжения для presentation-слоя. Значения не повторяют
/// состояния криптографических сессий: это фазы UI-флоу (Idle → ожидание ответа peer →
/// подтверждение кода → Completed/Failed), чтобы экран показывал одно согласованное состояние,
/// а не набор независимых флагов.
/// </summary>
public enum PairingFlowState
{
    /// <summary>Сценарий не начат.</summary>
    Idle = 0,

    /// <summary>Приглашение создано; ожидается ответ peer (для Child — QR-ответ Parent).</summary>
    WaitingForPeerResponse = 1,

    /// <summary>Код получен; пользователь сравнивает код на двух устройствах и подтверждает.</summary>
    AwaitingUserConfirmation = 2,

    /// <summary>Сопряжение завершено, доверенное устройство сохранено.</summary>
    Completed = 3,

    /// <summary>Сценарий завершился ошибкой или отменой; начать заново можно из Failed.</summary>
    Failed = 4
}
