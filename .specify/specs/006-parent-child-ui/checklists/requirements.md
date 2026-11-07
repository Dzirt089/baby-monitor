# Requirements Quality Checklist: 006-parent-child-ui

- [x] CHK-001 Scope UI ограничен; media/pairing-канал вне phase 1.
- [x] CHK-002 ADR-0006 Accepted до UI-реализации.
- [x] CHK-003 Явный выбор роли, Unknown недоступен.
- [x] CHK-004 Start/Stop только по действию пользователя; typed errors.
- [x] CHK-005 Parent не запрашивает RECORD_AUDIO; Child — по действию.
- [x] CHK-006 Composition root без static service locator; host Dispose.
- [x] CHK-007 UI не заявляет работающую передачу звука (media BLOCKED).
- [x] CHK-008 Strings в ресурсах, без секретов.
- [ ] CHK-009 Pairing UI/QR (phase 2).
- [-] CHK-010 Device permission/a11y/OEM — **BLOCKED**.
- [x] CHK-011 Трассируемость FR → tasks.md.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|---|