# Requirements Quality Checklist: 006-parent-child-ui

- [x] CHK-001 Scope UI ограничен; media/pairing-канал вне phase 1.
- [x] CHK-002 ADR-0006 Accepted до UI-реализации.
- [x] CHK-003 Явный выбор роли, Unknown недоступен.
- [x] CHK-004 Start/Stop только по действию пользователя; typed errors.
- [x] CHK-005 Parent не запрашивает RECORD_AUDIO; Child — по действию.
- [x] CHK-006 Composition root в App/Composition (per-Activity), host/VM Dispose.
- [x] CHK-007 UI не заявляет работающую передачу звука (media BLOCKED).
- [x] CHK-008 Strings/a11y — в ресурсах; ошибки без секретов.
- [x] CHK-009 Pairing UI/QR (phase 2): офлайн-обмен QR Child→Parent→Child + verification code.
- [-] CHK-010 Device permission/a11y/OEM — **BLOCKED**.
- [x] CHK-011 Трассируемость FR → tasks.md.
- [x] CHK-012 ADR-0011 Accepted до добавления ZXing.Net; лицензия/upstream/размер проверены.
- [x] CHK-013 QR приглашения не логируется/не показывается текстом; paste-поле очищается.
- [x] CHK-014 CAMERA запрашивается только по действию на pairing-экране; кадры не сохраняются.
- [x] CHK-015 Trusted devices: список/forget/повторное сопряжение; полный fingerprint не логируется.
- [x] CHK-016 Diagnostics screen без секретов и personal data.
- [x] CHK-017 ViewModels presentation-agnostic, покрыты fakes-тестами.
- [x] CHK-018 Архитектурные тесты: ZXing/camera только Platform.Android; Domain/Application чистые.
- [-] CHK-019 Camera/QR на реальных устройствах и TalkBack — **BLOCKED: нет устройств**.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|
| 2026-11-07 | reviewer (fresh independent) | APPROVE WITH FIXES → findings закрыты | 2 Major (OCE в DiagnosticsActivity; forget→Completed dead-end) + 9 Minor/Nit; все Major и ключевые Minor исправлены, повторные build/tests/emulator smoke зелёные |