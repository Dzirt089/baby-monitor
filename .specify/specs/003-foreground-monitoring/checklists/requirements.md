# Requirements Quality Checklist: 003-foreground-monitoring

- [x] CHK-001 Scope ограничен foreground monitoring; аудио/UI/pairing вне scope.
- [x] CHK-002 Каждый FR проверяем (тест/architecture test/документ).
- [x] CHK-003 Contract suite не зависит от Android и работает в CI.
- [x] CHK-004 Service не хранит Activity/View/ViewModel ссылок.
- [x] CHK-005 Start только из видимой Activity; typed background_start_denied.
- [x] CHK-006 POST_NOTIFICATIONS adapter + typed denial; notification содержит Stop.
- [x] CHK-007 Manifest permissions минимальны; RECORD_AUDIO объявлен app-wide (один APK), но запрашивается только для Child (Android 14+
  FGS microphone), Parent его не запрашивает; AudioRecord/AudioTrack отсутствуют (architecture test).
- [x] CHK-008 FGS-типы по роли (microphone/mediaPlayback) в merged manifest; architecture test.
- [x] CHK-009 Cleanup: Stop/OnDestroy снимают уведомление; host unbind/timeout.
- [-] CHK-010 Android 12 FGS/notification/screen-off/Stop PASS на 2 Huawei (2026-11-08);
  Activity destroyed, 13+/14+, Doze/OEM — **007**.
- [x] CHK-011 Трассируемость FR → tasks.md.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|---|
| | | | |