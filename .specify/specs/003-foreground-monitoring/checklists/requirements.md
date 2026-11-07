# Requirements Quality Checklist: 003-foreground-monitoring

- [x] CHK-001 Scope ограничен foreground monitoring; аудио/UI/pairing вне scope.
- [x] CHK-002 Каждый FR проверяем (тест/architecture test/документ).
- [x] CHK-003 Contract suite не зависит от Android и работает в CI.
- [x] CHK-004 Service не хранит Activity/View/ViewModel ссылок.
- [x] CHK-005 Start только из видимой Activity; typed background_start_denied.
- [x] CHK-006 POST_NOTIFICATIONS adapter + typed denial; notification содержит Stop.
- [x] CHK-007 Manifest permissions минимальны; microphone/audio permissions отсутствуют.
- [x] CHK-008 FGS-типы по роли (microphone/mediaPlayback); architecture test.
- [x] CHK-009 Cleanup: Stop/OnDestroy снимают уведомление; host unbind/timeout.
- [-] CHK-010 Device-проверки (Activity destroyed, 13+/14+, Doze/OEM) — **BLOCKED: нет устройств**.
- [x] CHK-011 Трассируемость FR → tasks.md.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|---|
| | | | |