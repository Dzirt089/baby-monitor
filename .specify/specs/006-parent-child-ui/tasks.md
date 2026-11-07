# Tasks: 006-parent-child-ui

Статусы: [ ] не начато, [x] завершено и проверено, [-] заблокировано (+причина).

## Phase 1 — Role selection + monitoring UI (merged, PR #13)

- [x] T001 ADR-0006 Accepted: .NET for Android (Activity+XML)
- [x] T002 PermissionActivity (IPermissionRequestHost + IActivityVisibility)
- [x] T003 MainActivity: выбор роли Child/Parent
- [x] T004 MonitoringActivity: start/stop/status/diagnostics через IForegroundSessionHost
- [x] T005 Layouts/strings (role + monitoring), typed errors
- [x] T006 Emulator runtime smoke: role selection → Child → Start → FOREGROUND service + notification (Радионяня: ребёнок / Мониторинг активен, ONGOING|FOREGROUND_SERVICE, action=Stop) → Stop → UI «Остановлен»; crash buffer пуст
- [x] T007 Local gates + PR #13 → CI → review → merge

## Phase 2 — Pairing UI/QR, diagnostics, ViewModels (этот PR)

- [x] T008 ADR-0011 Accepted (ZXing.Net 0.16.11, Apache-2.0) + THIRD-PARTY-NOTICES + package policy
- [x] T009 Application: PairingResponseQrCodec + PairingFlowState + PairedDeviceSummary
- [x] T010 Application: ChildPairingScreenModel + ParentPairingScreenModel (офлайн QR-обмен, expiry, confirm, dispose) + unit-тесты полного цикла/tamper/reject/store failure
- [x] T011 Application: TrustedDevicesScreenModel (refresh/forget) + DiagnosticsScreenModel/DiagnosticsSnapshot + тесты
- [x] T012 Platform.Android: QrCodeBitmapRenderer (ZXing) + QrCameraScanner (Camera2/YUV/LuminanceSource) + QrScannerErrorCodes + AndroidCameraPermission
- [x] T013 App: PairingActivity (Child QR/scan response/confirm; Parent scan/response QR/confirm; paste fallback; trusted state/forget/re-pair) + PairingComposition
- [x] T014 App: DiagnosticsActivity + DiagnosticsComposition + navigation (MainActivity → PairingActivity; MonitoringActivity → DiagnosticsActivity) + layout/strings/a11y
- [x] T015 Manifest: CAMERA runtime permission + uses-feature camera required=false; architecture tests (CAMERA, ZXing только Platform.Android, camera API только Qr, секреты не логируются)
- [x] T016 Local gates: build Debug/Release, все тесты, dotnet format
- [x] T017 Emulator smoke: QR render Child (screenshot), Parent camera open/close, paste fallback валидирует QR, pairing UI states, diagnostics, monitoring start/stop, crash buffer пуст
- [x] T018 Независимое ревью (fresh reviewer, APPROVE WITH FIXES → findings закрыты)
- [ ] T019 PR → CI green → merge в main; roadmap/CHANGELOG обновлены

## BLOCKED (внешние условия)

- [-] T020 Device matrix: 12+/13+/14+ permission flows, camera/OEM matrix, TalkBack/a11y — **BLOCKED: нет реальных устройств** (007)
- [-] T021 Реальный QR-обмен между двумя устройствами (камера↔камера), screen capture/OS-level — **BLOCKED: нет реальных устройств**
- [-] T022 Media/audio реальная передача звука — **BLOCKED: 005 phase 2 (ADR-0007 Proposed + нет устройств)**

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-07 | Emulator runtime smoke (AVD API 35, phase 1) | role selection OK; Child Start → foreground service + notification OK; Stop → UI Stopped; crash buffer пуст |
| 2026-11-07 | Local gates (006 phase 2) | Debug + Release build: 0 warnings, 0 errors; tests 370/370 (Domain 38, Application 216, Infrastructure 63, Architecture 53) через in-process раннер; dotnet test testhost в этой среде падает Win32Exception(5) Access denied до запуска тестов (ограничение sandbox, не кода); dotnet format --verify-no-changes: exit 0 |
| 2026-11-07 | Emulator smoke (AVD API 35, 006 phase 2) | Role selection; Child pairing: QR приглашения отрендерен (host ZXing decode OK), подтверждение host-ответа, code 917491 → completed, trusted store содержит запись LocalRole=Child; Parent pairing: paste QR приглашения → verification code 441255 + response QR (host verify signature OK) → confirm → trusted store LocalRole=Parent; invalid QR → typed pairing.invalid_qr; камера Camera2 открыта/остановлена без падения; Parent monitoring Start → FGS isForeground=true, types=0x2, notification «Радионяня: родитель / Мониторинг активен» ONGOING\|FOREGROUND_SERVICE; Stop → UI «Остановлен»; Diagnostics: роль/состояние/версия/доверенное устройство; crash buffer пуст |
| 2026-11-07 | Defect found & fixed during emulator smoke | Android Keystore SHA256withECDSA возвращает DER-подпись, BCL Ecdsa.VerifyData ожидает IEEE P1363 → реальное pairing падало invalid_signature при валидных ключах. Исправлено: PairingCrypto.TryVerifySignature принимает обе стандартные кодировки; regression tests PairingSignatureInteropTests (DER/P1363 обе стороны + tamper). Unit-тесты на BCL fake этого не ловили — дефект виден только при Android Keystore. |
| 2026-11-07 | Independent review (fresh reviewer, APPROVE WITH FIXES) | Закрыто: (1) OCE в async void DiagnosticsActivity → отдельный catch; (2) forget→Completed dead-end → ResetFlowModelAsync для обеих ролей; (3) блокирующие \_gate.Wait()/Task.Result → non-blocking/никогда .Result (screen models и QrCameraScanner), teardown без Join на main thread; (4) усилены architecture tests (ZXing source-scan вне Qr, camera permission call-site, secret-log case-insensitive); (5) удалены мёртвые строки, camera denial через resources, diagnostics_unexpected_error; (6) PairingResponseQrCodec limit согласован с ёмкостью QR + typed qr_render_failed; (7) trusted peer скрывает scan/paste/skip; (8) spec/AC/one-sided trust residual актуализированы. Повторный emulator smoke: forget→pairing-flow, QR host decode, camera open/stop, diagnostics open/back без crash; crash buffer пуст. |
| 2026-11-07 | BLOCKED remains | Реальный QR-обмен камера↔камера между двумя устройствами, camera/OEM matrix, TalkBack/a11y — **BLOCKED: нет реальных устройств** (007); media/audio — 005 phase 2 BLOCKED |