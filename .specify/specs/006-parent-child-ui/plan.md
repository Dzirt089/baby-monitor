# Implementation Plan: 006-parent-child-ui

**Spec**: [spec.md](./spec.md) | **ADR**: [ADR-0006](../../../docs/architecture/adr/0006-ui-framework.md) Accepted
**Status**: In Progress | **Created**: 2026-11-07

## Phases

1. **Phase 1 (этот PR)**: ADR-0006 Accepted (.NET for Android); PermissionActivity
   (IPermissionRequestHost + IActivityVisibility); MainActivity role selection; MonitoringActivity
   start/stop/status/diagnostics через IForegroundSessionHost; layouts/strings; emulator runtime smoke.
2. **Phase 2**: Pairing UI (Child QR/код, Parent ввод/scan, verification code confirm), diagnostics
   screen, ViewModels выделены в отдельные классы, a11y/локализация.
3. **Проверки**: build/tests/format; emulator UI smoke (реальный старт/стоп service); device matrix
   (permission flows, OEM) — **BLOCKED**.

## Technical Notes

- .NET for Android Activity+XML; ручной composition per Activity (без static service locator).
- PermissionActivity: один pending permission request, TaskCompletionSource, OnRequestPermissionsResult
  resolve, cancellation; повторный запрос при активном — ошибка.
- MonitoringActivity: AndroidForegroundSessionHost(this, notificationPermission, microphonePermission,
  this); Start/Stop async в UI event handlers; SnapshotChanged → RunOnUiThread; OnDestroy Dispose host
  (unbind), сервис продолжает работать независимо.
- Ошибки — typed code + message; секреты/аудио не логируются.
- Media/pairing-канал не реализованы: экран честно пишет, что передача звука недоступна до media.

## Risks

| Риск | Митигация |
|---|---|
| Permission dialog/recreate | bridge + повторное чтение snapshot после recreate |
| Activity leak в host | Dispose host в OnDestroy, сервис без Activity refs |
| Ложное ощущение работающего аудио | явный статус «media не реализована» (005 phase 2 BLOCKED) |
| OEM permission flows | device BLOCKED, 007 |

## Gate

Реальные устройства и media/audio (005 phase 2) отсутствуют; UI не заявляет работающую передачу звука.