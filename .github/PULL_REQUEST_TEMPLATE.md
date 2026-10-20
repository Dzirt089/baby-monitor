# Pull Request

## Summary

<!-- Кратко: что изменено и зачем -->

## Ссылки

- Feature spec: `.specify/specs/NNN-slug/spec.md`
- Issue: #
- ADR (если меняется архитектурное решение): `docs/architecture/adr/NNNN-...md`

## Изменения

<!-- Слои: Domain / Application / Infrastructure / Platform.Android / App / docs / CI -->

## Архитектура и границы

- [ ] Направление зависимостей не нарушено (Domain/Application не знают про Android/WebRTC/NSD).
- [ ] Android/WebRTC-типы не протекают в Application/Domain contracts.
- [ ] Composition root/DI остаётся в App/Composition.
- [ ] Новые пакеты обоснованы либо отсутствуют.

## Безопасность и приватность

- [ ] Инварианты ролей сохранены: Child send-only, Parent receive-only без RECORD_AUDIO.
- [ ] Нет записи/хранения/логирования аудио, секретов, tokens, полных fingerprints, private IP.
- [ ] Нет cleartext signaling/pairing и отключения TLS validation.
- [ ] Permissions минимальны и соответствуют сценарию.
- [ ] Для security-sensitive изменений (pairing/keys/TLS/transport/audio/service) описаны threat,
      trust boundary, failure mode и тесты.

## Тесты

- [ ] Добавлены/обновлены unit-тесты (и regression test для дефекта).
- [ ] Contract/architecture tests обновлены при изменении контрактов.
- [ ] `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build` выполнены локально.
- [ ] Что не проверено и почему (эмулятор/устройства/SDK) — указано ниже.

## Проверка на устройстве/эмуляторе

- Устройство/эмулятор и serial:
- Android API level:
- Сценарий и результат:

## Документация и релиз

- [ ] README/docs/ADR обновлены при изменении публичного контракта, протокола или эксплуатации.
- [ ] CHANGELOG обновляется только для выпущенных пользовательских изменений.
- [ ] Нет TODO/заглушек/закомментированного кода без согласования.
- [ ] Diff не содержит scope creep, секретов и машинных путей.

## Чек-лист перед merge

- [ ] CI зелёный.
- [ ] Ревью пройдено, замечания BLOCKER/MAJOR устранены.
- [ ] PR не создаёт/не двигает теги и не публикует релиз.
