# Feature Specification: 008 — Release readiness & distribution

**Feature branch**: `008-release-readiness` (планируется)
**Status**: **In Progress** (private side-load цепочка работает: v0.2.0/v0.3.0/v0.3.1 GitHub Releases;
остались signing-chain runbook/rotation, license/privacy/disclaimer, distribution decision — owner-gated)
**Created**: 2026-11-08
**Зависит от**: 000 (CI/release-by-tag), 007 (device hardening), ADR-0008 (side-load), ADR-0009
(versioning)

## Контекст

Репозиторий private; владелец разрешил side-load релизы v0.x по предохранителям (зелёные
локальные gates + merged PR + зелёный CI → SemVer-тег → GitHub Release с подписанным APK и SHA-256).
Уже выпущены v0.2.0 (pairing UI), v0.3.0 (signaling/media/audio), v0.3.1 (UI-fix); v0.3.1 подтверждена
на двух реальных устройствах (слышимость подтверждена владельцем). Публичная видимость, Google Play,
major-версия, удаление релизов/тегов и любые изменения secrets/keystore — **только с явным
подтверждением владельца** (AGENTS/предохранители).

008 формализует release-процесс и оставшиеся owner-gated решения: signing chain, secrets rotation,
store/дистрибуция, публичность, обновления, инцидент-реагирование, docs/CHANGELOG/license.

## Requirements

- **FR-001**: Релизы создаются только из merged main по SemVer-тегу `vMAJOR.MINOR.PATCH`; tag никогда
  не двигается/не пересоздаётся; release workflow проверяет CHANGELOG-раздел и signing secrets.
- **FR-002**: APK подписывается release keystore из GitHub Environment secrets; keystore/пароли не в
  репозитории/логах; GitHub Release публикует APK + `app-release.apk.sha256`; SHA-256 проверяется
  владельцем перед установкой.
- **FR-003**: Каждый релиз имеет: CHANGELOG с датой/секцией, обновлённые README/roadmap/device results,
  honest Known limitations (что не проверено), версию/versionCode по ADR-0009.
- **FR-004**: Release gates: build Debug/Release 0 warnings, все тесты, format, vuln scan, независимый
  review, зелёный CI; device-AC — с evidence, иначе BLOCKED/честно указано.
- **FR-005**: Обновления — side-load с GitHub Releases (ADR-0008), без auto-update; пользователь
  проверяет SHA-256 и ставит вручную; rollback — установка предыдущего релиза.
- **FR-006**: Signing chain: keystore владельца хранится вне репозитория; при смене keystore —
  процедура переустановки/миграции, documented; secrets rotation — только владелец, с проверкой
  release после ротации (dry-run).
- **FR-007**: Публичная видимость репозитория/релизов, Google Play/marketplaces, платные сервисы —
  отдельное явное решение владельца (в scope 008 только подготовка, не выполнение).
- **FR-008**: Major-версия (1.0.0+) и удаление тегов/релизов/веток с историей — только с явного
  подтверждения; агент не выполняет.
- **FR-009**: Юридическое/лицензионное: THIRD-PARTY-NOTICES актуален (включая SIPSorcery/Concentus/
  BouncyCastle/DnsClient и др.), лицензия проекта определена; privacy statement (аудио не пишется,
  нет аналитики/аккаунтов) — README/docs.
- **FR-010**: Инцидент-реагирование: при критическом дефекте — patch-релиз, отзыв релиза/тега только
  владельцем; честное уведомление в CHANGELOG/README; без force-push.
- **FR-011**: Release runbook/docs: как собрать/подписать/проверить/опубликовать, что делать при
  утечке secrets/keystore (ротация владельцем), как отозвать релиз (владелец).
- **FR-012**: Версионирование: 0.x — пока API/поведение меняются и device-матрица неполная; 1.0.0 —
  только после 007/008 exit criteria и решения владельца.

## Non-Functional

- **NFR-001**: Без секретов в git/логах/артефактах; release workflow использует secrets только для
  подписи и удаляет временный keystore.
- **NFR-002**: Релиз воспроизводим: tag → workflow → APK hash; локальная проверка SHA-256.
- **NFR-003**: Документация релиза честная: непроверенные device-сценарии/ограничения перечислены.
- **NFR-004**: Никаких публичных обещаний/заявлений «работающая радионяня» без owner confirmation
  слышимости на двух устройствах (достигнуто для v0.3.1 на Huawei; полная матрица — 007).

## Acceptance Criteria

- [x] AC-001: Release workflow по SemVer-тегу с проверкой CHANGELOG/secrets работает (v0.2.0/0.3.0/0.3.1).
- [x] AC-002: Подписанные APK + SHA-256 опубликованы в GitHub Releases; хэши совпадают при проверке.
- [x] AC-003: CHANGELOG/README/roadmap/device results обновляются в каждом релизе (v0.3.1 — device pass).
- [x] AC-004: Release gates (build/tests/format/vuln/CI/review) применяются; device-AC честно
  BLOCKED/подтверждены evidence.
- [x] AC-005: Side-load процесс ADR-0008 актуален; auto-update отсутствует.
- [ ] AC-006: Signing chain описан, keystore вне репозитория, secrets rotation procedure — готово к
  передаче владельцу; проверка ротации — только владелец.
- [ ] AC-007: THIRD-PARTY-NOTICES/лицензия проекта/privacy statement актуализированы для публичного
  распространения (если владелец решит публиковать).
- [ ] AC-008: Release runbook/incident response/rollback задокументированы.
- [ ] AC-009: Решение владельца по публичности/Play/major-версии зафиксировано (или явно отложено).
- [x] AC-010: Приложение не заявляется рабочей радионяней без owner sound confirmation; для v0.3.1
  подтверждение получено (Huawei pair), ограничения матрицы документированы.

## Вне scope

Публикация в Google Play/маркетах и изменение видимости репозитория (owner-only), удаление
релизов/тегов, major 1.0.0, платные сервисы/аккаунты — до явного решения владельца.

## Ссылки

- [Plan](plan.md) · [Tasks](tasks.md) · [Requirements checklist](checklists/requirements.md)
- [ADR-0008 side-load](../../../docs/architecture/adr/0008-app-update-strategy.md) ·
  [ADR-0009 versioning](../../../docs/architecture/adr/0009-android-versioning.md) ·
  [Release runbook](../../../docs/operations/release-runbook.md) ·
  [Device results 2026-11-08](../../../docs/operations/device-test-results-2026-11-08.md)