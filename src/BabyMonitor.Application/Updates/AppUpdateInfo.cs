using BabyMonitor.Domain.Updates;

namespace BabyMonitor.Application.Updates;

/// <summary>
/// Результат проверки обновлений: установленная версия и доступный манифест более новой версии.
/// Immutable-контракт для UI/ViewModel.
/// </summary>
/// <param name="CurrentVersion">Текущая установленная версия приложения.</param>
/// <param name="Manifest">Манифест доступного обновления.</param>
public sealed record AppUpdateInfo(AppVersion CurrentVersion, AppUpdateManifest Manifest);
