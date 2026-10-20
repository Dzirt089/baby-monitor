# Implementation Plan: 001-audio-transport-spike

**Spec**: [spec.md](./spec.md) | **Status**: In Progress | **Created**: 2026-10-20

## Summary

Разбить выбор media-библиотеки на фазы: (1) контракт порта и contract suite, (2) оценка пакета,
(3) device matrix, (4) ADR-0005. В этом PR выполняется phase 1 и инфраструктурная часть phase 2;
phase 3 блокирована отсутствием реальных устройств.

## Technical Context

- Порт: `BabyMonitor.Application.Abstractions.Realtime.*` (BCL only, никаких platform-типов).
- Реализации: только Infrastructure, за портом; test double — в тестовом проекте.
- Contract tests: `tests/BabyMonitor.Infrastructure.Tests/Realtime/RealtimeTransportContractTests.cs`
  (abstract base) + `FakeRealtimeTransport` + наследник для fake; будущие адаптеры добавляют наследника.
- Bounded queues: `System.Threading.Channels` с ограниченной ёмкостью и DropOldest (для реальных
  адаптеров); send/receive не блокируют поток транспорта бесконечно.
- Package policy: SIPSorcery добавляется только после phase 2/ADR-0005; лицензия/зависимости/scan
  фиксируются в spec/ADR до добавления в проект.
- CI: новый шаг `Test Infrastructure (contract tests)`; architecture tests проверяют состав solution.

## Constitution Check

- Принцип I (privacy): аудио не пишется/не логируется; port не содержит platform-типов.
- Принцип II (роли): send/receive разделяются на уровне сессии; Child/Parent логика появится в 003/005.
- Принцип III (зависимости): Application не знает библиотеку; проверяется architecture tests.
- Принцип IV (надёжность): типизированные ошибки, cancellation, bounded queues, состояния.
- Принцип V (честные проверки): device matrix не заявляется выполненной без реальных устройств.
- Принцип VI (security-sensitive): media-транспорт — security-sensitive; ADR-0005/0007 обновляются
  до production media.

## Phases

1. **Phase 1 (этот PR)**: порт, типы, contract suite на fake, solution/CI, architecture tests update.
2. **Phase 2**: оценка SIPSorcery (NuGet metadata/license/security/размер), spike harness
   (loopback/эмулятор), решение о добавлении пакета и экспериментальном адаптере.
3. **Phase 3 (blocked)**: device matrix по protocol; метрики на 2 реальных устройствах.
4. **Phase 4**: ADR-0005 Accepted/Rejected; после Accepted — реализация production adapter в 003/005.

## Risks

| Риск | Влияние | Митигация |
|---|---|---|
| Нет реальных устройств | phase 3/4 заблокированы | зафиксировать blocker, не имитировать измерения |
| SIPSorcery несовместим с net10.0-android/trimming | смена кандидата | phase 2 оценка, fallback — thin libwebrtc |
| Contract suite не покрывает реальные failure mode | ложное чувство готовности | расширять suite при появлении адаптера |
| Утечки/неограниченные буферы | деградация latency | bounded channels + review + stress-тесты |

## Gate

Production media-код и добавление media-пакета допускаются только после ADR-0005 Accepted и
отдельного подтверждения владельца.
