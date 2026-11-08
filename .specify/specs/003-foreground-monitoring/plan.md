# Implementation Plan: 003-foreground-monitoring

**Spec**: [spec.md](./spec.md) | **Status**: In Progress | **Created**: 2026-11-07

## Phases

1. **Контракт (выполнено)**: IForegroundSessionHost, INotificationPermission, ForegroundSessionSnapshot,
   MonitoringForegroundState, ForegroundSessionErrorCodes в Application; fake + contract suite.
2. **Android service (выполнено)**: MonitoringForegroundService (StartForeground с FGS-типом роли,
   notification channel, Stop action, launch intent), AndroidForegroundSessionHost (binder, видимость
   Activity, notification permission, timeout/cleanup), IActivityVisibility + MainActivity реализация,
   manifest permissions, architecture tests.
3. **Проверки**: build/tests/format; emulator smoke; device-проверки на 2 Huawei/Android 12 — PASS
   (FGS/notification/screen-off/Stop, 2026-11-08); 13+/14+/Doze — 007; PR → CI → review → merge.

## Technical Notes

- Один service с `foregroundServiceType="microphone|mediaPlayback"`; StartForeground получает тип по роли:
  Child => Microphone (API 30+), Parent => MediaPlayback (API 29+); legacy fallback для API 24–29.
- API 34+: StartForeground типизирован; при отсутствии type-константы на старых API используется
  untyped overload (Android 10 разрешает).
- Host проверяет IActivityVisibility (MainActivity OnResume/OnPause), POST_NOTIFICATIONS и привязку
  к service; снимки immutable, состояние шарится через binder + SnapshotChanged.
- Notification не содержит персональных данных; Stop доступен без открытия Activity.
- Activity/composition wiring — 006; до этого host адаптер не подключён (dead code осознанно).

## Risks

| Риск | Митигация |
|---|---|
| Android 12+ запрет старта FGS из фона | проверка видимости Activity + typed error |
| Android 14+ типы FGS/while-in-use | типы по роли, permission adapter, device tests BLOCKED |
| POST_NOTIFICATIONS отказ | typed notification_permission_denied до старта |
| Уничтожение процесса/Activity | service независим, activity не хранится; emulator smoke частично |
| Преждевременное аудио в 003 | сервис не открывает микрофон/playback до 005 |

## Стратегия проверки platform-only путей

Activity visibility, POST_NOTIFICATIONS/RECORD_AUDIO запросы, Android 14+ FGS restrictions и
notification работают только на Android. Contract suite в CI (net10.0) проверяет fake-хост;
Android-адаптер покрыт build, merged-manifest architecture tests, review и emulator install smoke.
Реальный старт service/notification проверен через UI на 2 Huawei (2026-11-08); Android 13+/14+/Doze — 007.
MainActivity реализует IActivityVisibility, но app-scoped host в 006 не должен хранить Activity
ссылку: нужен app-scoped visibility-state или weak reference (зафиксировано для 006).

## Gate

Device-проверки не заявляются выполненными без реальных устройств.