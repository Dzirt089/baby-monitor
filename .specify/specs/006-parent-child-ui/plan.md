# Implementation Plan: 006-parent-child-ui

**Spec**: [spec.md](./spec.md) | **ADR**: [ADR-0006](../../../docs/architecture/adr/0006-ui-framework.md) Accepted,
[ADR-0011](../../../docs/architecture/adr/0011-qr-encoding-decoding-library.md) Accepted (ZXing.Net)
**Status**: Phase 2 реализована в PR #14 (independent review APPROVE WITH FIXES → findings закрыты; device matrix и real 2-device QR BLOCKED) | **Created**: 2026-11-07

## Phases

1. **Phase 1 (merged, PR #13)**: ADR-0006 Accepted (.NET for Android); PermissionActivity
   (IPermissionRequestHost + IActivityVisibility); MainActivity role selection; MonitoringActivity
   start/stop/status/diagnostics через IForegroundSessionHost; layouts/strings; emulator runtime smoke.
2. **Phase 2 (этот PR)**: pairing UI (Child invite QR → Parent scan+response QR → Child scan ответа),
   verification code confirm, trusted devices (просмотр/forget/повторное сопряжение), diagnostics
   screen, ViewModels в отдельных presentation-agnostic классах Application, a11y/локализация.
3. **Проверки**: build/tests/format; emulator UI smoke (QR render, camera open, pairing UI,
   monitoring start/stop); реальный QR-обмен между двумя устройствами и camera/OEM matrix —
   **BLOCKED: нет реальных устройств**.

## Phase 2 Design Decisions

- **Офлайн QR-обмен вместо сети**: 005 phase 2 (signaling/media) BLOCKED (ADR-0007 Proposed, нет
  устройств), поэтому 004-протокол завершается out-of-band через физические экраны: Child
  показывает QR приглашения (секрет, TTL 5 минут), Parent его сканирует и показывает QR ответа
  (только публичные данные), Child сканирует ответ и выводит симметричный verification code.
  Сеть не используется; signaling для runtime-сессии остаётся в 005.
- **Application screen models** (MVVM, presentation-agnostic): ChildPairingScreenModel,
  ParentPairingScreenModel, TrustedDevicesScreenModel, DiagnosticsScreenModel; Activity только
  связывает View↔команды и владеет Android-ресурсами (camera, QR bitmap, strings).
- **PairingResponseQrCodec** (Application): версионированный бинарный payload в base64url,
  length-prefixed поля, лимиты, валидация SPKI; только публичные данные Parent. Private key в QR
  отсутствует; QR приглашения считается секретом и не логируется.
- **QR rendering**: QrCodeBitmapRenderer (Platform.Android, ZXing.Net) → Android Bitmap;
  ECC M, quiet zone 2, размер 720px (кламп 256..2048).
- **Camera scanning**: Camera2 + ImageReader YUV_420_888 (platform API, без AndroidX); декодирование
  QR на background HandlerThread; кадры не сохраняются, не логируются; после распознавания камера
  останавливается; Stop/Dispose/OnPause освобождают камеру. Permission CAMERA — runtime через
  существующий permission bridge.
- **Fallback**: «вставьте QR-текст» — диагностический путь для устройств без камеры/эмулятора; та же
  криптовалидация, поле маскируется и очищается; доверие требует code+подтверждения. Fallback не
  является основным путём и не заявляется как замена камере.
- **Navigation**: MainActivity (роль) → PairingActivity; вошедший с доверенным устройством видит
  статус «сопряжено» (к мониторингу / забыть / сопрячь заново) и честный Skip к мониторингу;
  MonitoringActivity → DiagnosticsActivity.
- **Composition**: App/Composition per-Activity (PairingComposition, DiagnosticsComposition);
  AndroidKeystorePairingKeyStore + AndroidSecureEntropy + AndroidTrustedDeviceStore + TimeProvider.System.
- **Секреты**: QR payload/token/verification code не логируются и не отображаются текстом; в
  PairingActivity paste-поле очищается после применения; android:allowBackup=false сохраняется.
- **Ошибки**: typed коды из PairingErrorCodes + QrScannerErrorCodes; пользовательское сообщение —
  из строк/resources; технические причины не содержат секретов.

## Files (phase 2)

- Application: Pairing/PairingFlowState.cs, Pairing/PairingResponseQrCodec.cs,
  Pairing/ChildPairingScreenModel.cs, Pairing/ParentPairingScreenModel.cs,
  Pairing/TrustedDevicesScreenModel.cs, Pairing/PairedDeviceSummary.cs,
  Diagnostics/DiagnosticsScreenModel.cs, Diagnostics/DiagnosticsSnapshot.cs;
  константы PairingConstants.ResponseQrPrefix, PairingErrorCodes.Cancelled.
- Platform.Android: Qr/QrScannerErrorCodes.cs, Qr/QrCodeBitmapRenderer.cs,
  Qr/QrCameraScanner.cs, Permissions/AndroidCameraPermission.cs; ZXing.Net 0.16.11
  (ADR-0011).
- App: PairingActivity, DiagnosticsActivity, Composition/PairingComposition,
  Composition/DiagnosticsComposition, layouts activity_pairing/activity_diagnostics,
  strings/a11y, CAMERA manifest permission + uses-feature camera required=false, навигация.
- Tests: Application unit-тесты (response QR codec, screen models, полный офлайн-цикл, expiry,
  tamper, reject, store failure, dispose, trusted/diagnostics), Architecture tests (CAMERA,
  ZXing только Platform.Android, camera API только в Qr, секреты не логируются).
- Docs: spec/plan/tasks/checklist, ADR-0011, pairing threat model update, THIRD-PARTY-NOTICES,
  roadmap/CHANGELOG после merge.

## Risks

| Риск | Митигация |
|---|---|
| Camera2 lifecycle/утечки на разных OEM | Stop/Dispose/OnPause, emulator smoke; device matrix BLOCKED (007) |
| QR decode на реальных камерах не проверен | ZXing.Net зрелая библиотека + fallback; device E2E BLOCKED |
| Секреты (token/code) в логах/строках | тесты secret-scan; запрет печати payload; paste-поле маскируется |
| Ложное ощущение рабочей радионяни | явный статус «media не реализована (005 BLOCKED)» на pairing/monitoring/diagnostics |
| Новый NuGet (ZXing.Net) | ADR-0011, централизованная версия, лицензия/upstream/размер/безопасность проверены |
| Офлайн-обмен не заменяет signaling | явно в spec/UI: pairing завершён, передача звука/переподключение — 005 |
| Activity recreate во время pairing | сессия в screen model; при recreate старый flow пересоздаётся (QR/код regenerated) — документировано |
| CAMERA permission в одном APK для обеих ролей | camera только на pairing-экране по действию; manifest uses-feature required=false |
| Camera preview orientation/aspect на OEM | preview может быть повёрнут/растянут (transform не делается); QR decode от этого не зависит; device matrix BLOCKED (007) |
| MVP: один доверенный peer | при наличии trust UI скрывает scan/paste/skip и работает с первым peer; несколько записей store возможны только через явное «Сопрячь заново» — единый peer-полиси для 007/008 |

## Gate

Build/tests/format/emulator smoke обязательны. Реальный QR-обмен двух устройств, camera matrix,
TalkBack/OEM, media/audio — **BLOCKED: нет реальных устройств** (007) / 005 phase 2 BLOCKED.
UI не заявляет передачу звука, сетевую связь или повторное подключение.