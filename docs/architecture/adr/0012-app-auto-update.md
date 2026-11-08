# ADR-0012: Автообновление приложения через публичные GitHub Releases

**Status**: Accepted (owner selected option B: public repo + auto-update)
**Date**: 2026-11-08

## Context

Тестовые сборки side-load через GitHub Releases. Владелец хочет проверку обновлений по кнопке и при
запуске, с проверкой версии/SHA-256/подписи, скачиванием и подтверждаемой установкой, Wi-Fi-only,
раз в сутки, отключаемо. Репозиторий стал public явным решением владельца. Вшивание GitHub token в
APK запрещено (утечка секрета). Play-канал (008) пока недоступен.

## Decision

1. Источник — публичные GitHub Releases: asset update-manifest.json в latest release, APK — соседний
   asset. URL без API/токена:
   https://github.com/Dzirt089/baby-monitor/releases/latest/download/update-manifest.json
2. Манифест schemaVersion=1: packageId, versionName, versionCode, apkAssetName, sha256, sizeBytes,
   notes, publishedAt.
3. Клиент: HTTPS system trust; проверка packageId/schema; versionCode strictly greater; скачивание
   только Wi-Fi; сверка размера/SHA-256; проверка signing certificate SHA-256 APK == установленной;
   установка только через PackageInstaller с подтверждением пользователя.
4. Авто-проверка: при запуске, не чаще 1/24h, только Wi-Fi, отключаема; UI-диалог при наличии
   обновления. Silent install запрещён.
5. Release workflow генерирует и прикладывает манифест к каждому release, начиная с v0.5.0.

## Consequences

+ Нет серверной инфраструктуры/токенов; GitHub Releases уже подписаны и checksum.
+ SHA-256 + signature match + user confirmation дают приемлемую защиту от подмены.
− Зависимость от GitHub availability/UX; private→public code visibility (owner approved).
− Huawei unknown-sources UI требует device-проверки.
− Auto-check только online-режим; при недоступности GitHub — typed error/retry позже.

## Threat model / trust boundary

- **Manifest не подписан**. Компрометация GitHub-аккаунта/репозитория может подменить manifest (version/
  sha256/size/notes). Trust anchor — подпись APK: перед установкой signing cert SHA-256 APK сравнивается
  с установленным приложением; чужой APK установить нельзя. Подмена version/size ограничивается
  MaxApkSizeBytes/versionCode/downgrade, но compromised manifest может показать ложную versionName —
  accept risk до подписания manifest (future work).
- **HTTPS/redirect**: system trust; исходный URL https github.com; финальный RequestUri после redirect
  проверяется на https + host allowlist (github.com/githubusercontent). При компрометации CA/канала
  signature check остаётся trust anchor; cleartext запрещён.
- **Signing-key compromise**: обновление может установить кто угодно с ключом release; ротация ключа —
  owner-gated 008; signature mismatch by design при смене ключа.
- **Disk exhaustion**: MaxApkSizeBytes (200 MB) и лимит manifest 64 KiB; temp cleanup.
- **User confirmation**: PackageInstaller PendingUserAction intent запускается приложением; silent install
  невозможен; unknown-sources проверяется.

## Alternatives

- Play In-App Updates (008/Play) — позже.
- Собственный HTTPS feed — лишняя инфраструктура.
- Вшить GitHub token для private repo — запрещено (секрет в APK).