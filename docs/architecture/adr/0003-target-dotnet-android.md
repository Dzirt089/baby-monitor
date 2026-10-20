# ADR-0003: Целевая платформа — .NET 10 Android, минимальный API 24

- **Статус**: Accepted
- **Дата**: 2026-10-20
- **Решение принимает**: ведущий архитектор
- **Связанные документы**: [AGENTS.md](../../../AGENTS.md) раздел 4, [ADR-0006](0006-ui-framework.md), [global.json](../../../global.json)

## Контекст

Нужен C#-стек для приложения-радионяни на Android с доступом к AudioRecord/AudioTrack, NSD,
foreground service, Keystore и WebRTC. Машина разработки: .NET SDK 10.0.400, workload android
36.1.69, Android SDK platform android-36, JDK 17+ (локально проверено на JDK 21). MAUI Android
workload не установлен.

## Решение

- TFM Android-проектов: net10.0-android; library-проекты (Domain/Application/Infrastructure,
  тесты) — net10.0.
- Минимальная поддерживаемая версия: API 24 (SupportedOSPlatformVersion=24).
- SDK жёстко фиксируется global.json (10.0.400, rollForward latestFeature).
- Версии пакетов — централизованно в Directory.Packages.props (CPM).
- Android-сборка воспроизводится через dotnet workload install android; MAUI-нагрузка не требуется.
- Bootstrap использует чистый .NET for Android. Выбор UI-фреймворка — отдельный Proposed ADR-0006.

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Почему отклонена |
|---|---|---|---|
| net9.0-android | стабильнее экосистема | устаревает, нет текущего LTS-вектора проекта | выбран актуальный поддерживаемый SDK |
| Kotlin/нативный Android | лучшая поддержка Android API | нет C#-переиспользования домена, команда ориентирована на .NET | AGENTS.md фиксирует C# |
| MAUI как обязательный стек | единый UI-стек | workload не установлен, избыточен для 2 экранов до spike | решение по UI — ADR-0006 (Proposed) |
| API 21/23 минимум | шире охват устройств | меньше гарантий по runtime/security | API 24 — разумный компромисс MVP |

## Последствия

- Единый APK собирается из src/BabyMonitor.App (ApplicationId на bootstrap — com.example.babymonitor;
  финальный id утверждён в [ADR-0010](0010-final-application-id.md)).
- На CI требуется dotnet workload install android (ubuntu-latest; Temurin 21, минимально JDK 17+).
- Ограничение API 24 сужает device matrix; для release readiness нужны реальные устройства
  разных производителей и API levels (см. AGENTS.md раздел 16).

## Проверка и критерии пересмотра

Architecture.Tests проверяют TFM/ApplicationId/SupportedOSPlatformVersion. Пересмотр — при
невозможности использовать нужное Android API на API 24 или при изменении политики поддержки SDK.
