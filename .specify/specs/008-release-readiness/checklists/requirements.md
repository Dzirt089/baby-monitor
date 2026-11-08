# Requirements Checklist: 008 — Release readiness

- [x] Side-load release цепочка (tag→workflow→signed APK+hash→GitHub Release) работает и проверена
  на v0.2.0/v0.3.0/v0.3.1.
- [x] Release gates: build/tests/format/vuln/CI/review; device-AC честно фиксируются.
- [x] Никаких secrets/keystore в репозитории/логах; временный keystore удаляется workflow.
- [x] Side-load ADR-0008, versioning ADR-0009; auto-update отсутствует.
- [x] Owner-gated пункты явно отделены (публичность/Play/major/удаление/secrets) — агент не выполняет.
- [ ] License/privacy/disclaimer документы доработаны для возможной публикации (owner decision).
- [ ] Release runbook/incident/rollback проверены владельцем.
- [ ] Решение владельца по публичности/Play/major зафиксировано.
- [x] Device evidence v0.3.1 (owner-confirmed sound) отражена в docs.
- [x] Ссылки на spec/plan/tasks/ADR/runbook/device results.
