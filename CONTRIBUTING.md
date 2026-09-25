# Katkı Rehberi

## İlk kurulum

```bash
git config core.hooksPath .githooks      # commit mesajı doğrulaması
git config commit.template .gitmessage   # commit mesajı şablonu
```

## Dal stratejisi

```
main          ← her zaman derlenir ve yayınlanabilir; doğrudan commit yapılmaz
feature/*     ← yeni özellik        feature/effective-config-view
fix/*         ← hata düzeltmesi     fix/bom-lost-on-save
hotfix/*      ← yayındaki kritik hata  hotfix/v0.1.1
release/*     ← sürüm hazırlığı     release/v0.2.0
docs/*, refactor/*, chore/*
```

- Dal adları küçük harf, tire ile ayrılmış, 2–5 kelime, en fazla 60 karakter. Varsa issue numarası eklenir: `fix/42-bom-lost-on-save`.
- Feature/fix dalları `main`'e **squash merge** ile girer; PR başlığı squash commit mesajı olur.
- Release ve hotfix dalları `main`'e **merge commit** ile girer ve etiketlenir.
- Birleştirilen dallar silinir. `main`'e force-push yapılmaz.

## Commit mesajı (Conventional Commits)

```
<type>(<scope>): <kısa açıklama>

[neden ve ne değişti — nasıl değil]

[Closes #12 | BREAKING CHANGE: ...]
```

| type | ne zaman | sürüm etkisi |
|---|---|---|
| `feat` | kullanıcıya yeni özellik | MINOR |
| `fix` | hata düzeltmesi | PATCH |
| `perf`, `refactor`, `style`, `test`, `docs`, `build`, `ci`, `chore`, `revert` | diğer | yok |

- Kapsamlar: `core`, `app`, `tests`, `docs`, `ci`, `release`, `deps`
- Başlık: emir kipi, küçük harfle başlar, sonda nokta yok, ≤ 72 karakter.
- `BREAKING CHANGE:` footer'ı MAJOR sürüm demektir ve geçiş notu içermelidir.
- `.githooks/commit-msg` bu kuralları yerelde, PR iş akışı PR başlığında denetler.

## CHANGELOG kuralları

- Biçim: [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/). Yeni kayıtlar her zaman `## [Unreleased]` altına yazılır.
- `feat` ve `fix` PR'ları CHANGELOG'u **aynı PR'da** günceller (CI denetler).
- Bölümler: `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`. Boş bölüm yazılmaz.
- Kayıt kullanıcının göreceği değişikliği anlatır, kodu değil. Varsa issue/PR numarası eklenir.

## Pull request

1. `main`'den dal açın, küçük ve tek konulu tutun (ideal < 400 satır).
2. `dotnet format`, `dotnet build`, `dotnet test` yerelde geçmeli.
3. PR şablonundaki kontrol listesini doldurun. CI yeşil olmadan birleştirilmez.

## Sürüm yayınlama

Sürüm numarası tek yerde tutulur: `Directory.Build.props` → `<Version>`.

```powershell
git switch main; git pull
./scripts/release.ps1 -Version 0.2.0      # release/v0.2.0 dalı, sürüm + CHANGELOG commit'i
# PR açın → CI yeşil → main'e merge commit ile birleştirin, ardından:
git switch main; git pull
git tag -a v0.2.0 -m "Release v0.2.0"
git push origin v0.2.0                     # release iş akışı paketleri üretir ve GitHub Release oluşturur
```

Hotfix: `main`'den `hotfix/v0.2.1` dalı → düzeltme + `./scripts/release.ps1 -Version 0.2.1 -NoBranch` → merge → etiket.
