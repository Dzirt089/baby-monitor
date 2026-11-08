# Requirements Quality Checklist: 005-session-reliability

- [x] CHK-001 Scope reliability/media/android audio ограничен; video/cloud вне scope.
- [x] CHK-002 Каждый FR проверяем (unit-тест/architecture test/документ).
- [x] CHK-003 КА: allowed/forbidden переходы и терминальность протестированы.
- [x] CHK-004 Heartbeat и media flow различаются (причины/health независимы).
- [x] CHK-005 Reconnect: backoff+jitter+final cap+max attempts (CanAttempt/GetDelay); детерминизм.
- [x] CHK-006 Parent alarm локален и не зависит от Child; событие/сброс.
- [x] CHK-007 Роли: Child capture only, Parent playback only (КА/health тесты).
- [x] CHK-008 Нет аудио/секретов в логах — architecture tests (MediaPipelineArchitectureTests) PASS.
- [x] CHK-009 SIPSorcery только в Infrastructure; contract suite + media loopback PASS.
- [x] CHK-010 Android audio adapters + bounded buffers реализованы и проверены на реальных Huawei.
- [x] CHK-011 Device: Wi-Fi loss/reconnect/screen-off/Stop PASS (2 Huawei, 2026-11-08); 100 циклов/soak/звонки — 007.
- [x] CHK-012 Трассируемость FR → tasks.md.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|---|