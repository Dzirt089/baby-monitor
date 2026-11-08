# Requirements Checklist: 007 — Device hardening

- [x] Scope ограничен hardening/soak/a11y/field-проверками, без расширения MVP (AGENTS §3).
- [x] Security/privacy: нет записи/логов аудио, нет отключения TLS/pinning, counters без секретов.
- [x] Архитектурные границы: heartbeat/counters — Application-порты; Android/SIPSorcery — только
  нижние слои; audio adapters только Platform.Android/Audio.
- [x] Device-проверки: результаты фиксируются с моделями/API/APK hash; непроверенное не заявляется.
- [x] OEM/Doze: корректность не строится только на whitelist-инструкции (AGENTS §9).
- [x] Планы тестов: unit (fake TimeProvider), contract (loopback), device (checklist), soak.
- [x] Релизный процесс: review + CI + tag; публичная дистрибуция — 008/owner.
- [x] Ссылки на spec/plan/tasks/device results/threat model.

## Открытые вопросы

- [ ] Нужны ли отдельные устройства Pixel/Samsung/Xiaomi (нет у владельца) — OEM-матрица может
  остаться частичной/BLOCKED.
- [ ] Политика heartbeat-интервала/порога тревоги (по умолчанию 10 с heartbeat, 2 с media) —
  подтвердить на device-прогоне.
