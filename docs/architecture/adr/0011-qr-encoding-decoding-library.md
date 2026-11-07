# ADR-0011: QR-кодирование и сканирование для сопряжения — ZXing.Net

- **Статус**: Accepted (2026-11-07)
- **Дата**: 2026-11-07
- **Решение принимает**: ведущий архитектор (автономная реализация roadmap 006 по поручению владельца); security review — в PR 006 phase 2
- **Связанные документы**: [ADR-0006](0006-ui-framework.md), [ADR-0007](0007-local-security-signaling-pairing.md) (Proposed),
  [pairing threat model](../../security/pairing-threat-model.md), [spec 006](../../../.specify/specs/006-parent-child-ui/spec.md),
  [AGENTS.md](../../../AGENTS.md) раздел 4

## Контекст

006 phase 2 добавляет pairing UI по 004-протоколу: Child показывает QR приглашения, Parent сканирует
его и показывает QR ответа, Child сканирует ответ. Сетевой signaling-канал (005 phase 2) остаётся
**BLOCKED** (ADR-0007 Proposed, нет устройств), поэтому обмен выполняется out-of-band через
физические экраны. Нужны:

- генерация QR из строки payload (приглашение ~400–500 символов base64url; ответ ~350 символов);
- декодирование QR с камеры на устройстве (Camera2, API 24+), без облака/Google Play Services;
- работа полностью офлайн, без сетевых вызовов и без сохранения изображений;
- лицензия, совместимая с распространением APK; проверка по AGENTS.md §4
  (необходимость, актуальность, лицензия, Android-совместимость, security, изоляция пакета).

Android platform API не содержит QR encoder/decoder. CameraX/AndroidX не используется: камера
реализуется на platform Camera2 + ImageReader (меньше зависимостей и размер APK).

## Решение (принято)

Использовать **ZXing.Net 0.16.11** (NuGet, Apache-2.0) **только в BabyMonitor.Platform.Android**:

- QrCodeBitmapRenderer — ZXing QRCodeWriter → Bitmap (ECC M, quiet zone 2, 720 px);
- QrCameraScanner — Camera2 + ImageReader YUV_420_888 → собственный LuminanceSource →
  ZXing BarcodeReaderGeneric (QR only, TryHarder) на background HandlerThread;
- пакет изолирован внутренними классами Platform.Android.Qr; Application/Domain/App не ссылаются
  на ZXing; версия — только в Directory.Packages.props; лицензия — в THIRD-PARTY-NOTICES.

Проверка пакета по AGENTS.md §4:

| Критерий | Проверка |
|---|---|
| Зачем нужен | QR encode/decode нельзя писать самостоятельно без высокого риска ошибок; platform API отсутствует; ML Kit требует Play Services/сеть |
| Актуальность | upstream <https://github.com/micjahn/ZXing.Net> активен (коммиты 2026, релиз 0.16.11) |
| Лицензия | Apache-2.0, без copyleft; совместима с проектом; уведомление добавлено |
| Android-совместимость | pure managed (netstandard2.0/net9.0 assets), без native libs; работает на net10.0-android; сам пакет не требует permissions |
| Влияние на APK | managed-сборка, без native; добавляется только в Platform.Android; Release-сборка проверяется в CI |
| Security history | нет известных уязвимостей в NuGet advisories (проверка dotnet list package --vulnerable в CI); декодирование недоверенного изображения ограничено кадром камеры, а результат проходит криптовалидацию 004-протокола до доверия |
| Скрытие за портом | внутренние классы Platform.Android.Qr; App зависит только от них |

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Почему отклонена |
|---|---|---|---|
| Собственный QR encoder/decoder (Reed-Solomon, маски) | нет зависимостей | сложно, риск ошибок, нужен security review алгоритма | запрещено «изобретать» без ADR и доказательств |
| ML Kit Barcode Scanning | качество Google | Play Services/облачная обработка, офлайн-гарантии, приватность, размер | несовместимо с offline/privacy-моделью |
| ZXing.Net.Mobile / старые Xamarin bindings | готовый кейс | устаревшая поддержка, лишние Android-зависимости | хуже по AGENTS §4 (поддержка) |
| Сканирование через внешнее приложение (Intent) | нет зависимостей | нет гарантии установки/офлайн, payload покидает приложение | не гарантирует сценарий |
| Ручной ввод текста payload | без камеры | payload ~400+ символов — непригодно как основной путь | оставлен только диагностический paste-fallback |

## Последствия

- Полноценное офлайн-сопряжение двух устройств без сети и без Google Play Services.
- APK увеличивается на managed-сборку ZXing; Release/linker поведение проверяется в CI.
- В manifest появляется runtime-permission CAMERA (используется только pairing-экраном по явному
  действию; код камеры — только Platform.Android.Qr).
- Реальный QR decode на камерах разных OEM не проверен: **BLOCKED: нет реальных устройств**; на
  эмуляторе проверяется открытие/закрытие камеры и paste-fallback; decoding correctness
  проверяется host-side spike и unit-тестами codecs.
- Собственная криптография не добавляется: QR — только транспорт/кодирование публичных данных и
  короткоживущего token; проверки подписей/ECDH остаются в Application (004).

## Проверка и критерии пересмотра

- Architecture tests: ZXing.Net PackageReference только в Platform.Android; camera API только в
  Platform.Android/Qr; CAMERA в manifest + uses-feature camera required=false; секреты не логируются.
- Build Debug/Release + emulator smoke; host-side spike encode→decode.
- Пересмотр: если decode rate/стабильность на реальных устройствах окажется недостаточной
  (007 device matrix) — рассмотреть ML Kit/ZXing.Net.Mobile с новым ADR и security review.

## Ссылки

- Feature: [006 spec](../../../.specify/specs/006-parent-child-ui/spec.md), [plan](../../../.specify/specs/006-parent-child-ui/plan.md)
- Security: [pairing threat model](../../security/pairing-threat-model.md)
- Package: <https://www.nuget.org/packages/ZXing.Net/0.16.11>
