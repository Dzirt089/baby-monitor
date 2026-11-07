# Requirements Quality Checklist: 005-session-reliability

- [x] CHK-001 Scope reliability/media/android audio ограничен; video/cloud вне scope.
- [x] CHK-002 Каждый FR проверяем (unit-тест/architecture test/документ).
- [x] CHK-003 КА: allowed/forbidden переходы и терминальность протестированы.
- [x] CHK-004 Heartbeat и media flow различаются (причины/health независимы).
- [x] CHK-005 Reconnect: backoff+jitter+cap+max attempts; детерминизм.
- [x] CHK-006 Parent alarm локален и не зависит от Child; событие/сброс.
- [x] CHK-007 Роли: Child capture only, Parent playback only (КА/health тесты).
- [ ] CHK-008 Нет аудио/секретов в логах (проверяется в phase 2 для adapters).
- [ ] CHK-009 SIPSorcery только в Infrastructure, contract suite (phase 2).
- [ ] CHK-010 Android audio adapters + bounded buffers (phase 2).
- [-] CHK-011 Device: 100 циклов/soak/Wi-Fi loss/звонки — **BLOCKED: нет устройств**.
- [x] CHK-012 Трассируемость FR → tasks.md.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|---|