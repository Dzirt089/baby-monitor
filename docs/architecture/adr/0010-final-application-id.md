# ADR-0010: Финальный ApplicationId — io.github.dzirt089.babymonitor

- **Статус**: Accepted
- **Дата**: 2026-10-20
- **Решение принимает**: владелец репозитория Dzirt089 (поручение Lead-агенту)
- **Связанные документы**: [ADR-0003](0003-target-dotnet-android.md), [README.md](../../../README.md), [release.yml](../../../.github/workflows/release.yml)

## Контекст

Bootstrap-фаза использовала placeholder `com.example.babymonitor` из стартового промпта. После
первой публикации смена ApplicationId означает фактически другое приложение: обновления на
установленные сборки не встанут, а Google Play считает package name глобально уникальным.
Основной сценарий MVP — side-load/локальное распространение, Play не требуется; нужен стабильный
id, не зависящий от покупки домена и не использующий зарезервированный `com.example.*`.

## Решение

- Финальный ApplicationId: **io.github.dzirt089.babymonitor**.
- Основание: reverse-DNS от верифицированного владельца GitHub-репозитория (Dzirt089);
  namespace `io.github.*` общепринят для open-source/side-load приложений и не требует домена.
- Значение фиксируется в `src/BabyMonitor.App/BabyMonitor.App.csproj` как единственный источник
  истины. Release workflow читает ApplicationId из csproj (`dotnet msbuild -getProperty:ApplicationId`)
  и сверяет с упакованным APK; Architecture.Tests проверяют ожидаемое значение.
- Изменение id после первого публичного APK допускается только новым ADR (это новое приложение
  и новая цепочка обновлений).

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Почему отклонена |
|---|---|---|---|
| Оставить `com.example.babymonitor` | ничего не менять | зарезервированный namespace, непригоден для постоянного id | не финальный идентификатор |
| `com.dzirt089.babymonitor` | коротко | нет namespace-гарантии, возможны конфликты | `io.github.*` проверяем по владельцу |
| Собственный домен (`ru.example.babymonitor`) | полный контроль | требует покупки/владения доменом | избыточно для MVP |
| Смена id после первого релиза | «потом решим» | ломает обновления и Play-историю | противоречит release readiness |

## Последствия

- Debug/release APK получают имя `io.github.dzirt089.babymonitor[-Signed].apk`.
- Документация, architecture tests и release-проверка синхронизированы с новым id.
- При возможной публикации в Google Play потребуется заранее подтвердить/занять этот package name.
- До первого релиза id уже финальный; дальнейшая смена — только новым ADR.

## Проверка и критерии пересмотра

Architecture.Tests (`TargetFrameworkTests`) проверяют ApplicationId в csproj; release workflow
сверяет ApplicationId из csproj с APK. Пересмотр — только новым ADR при вынужденной смене
распространения (например, требование Play).