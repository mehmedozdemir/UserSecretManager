# Changelog

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.

- Biçim: [Keep a Changelog 1.1.0](https://keepachangelog.com/tr/1.1.0/)
- Sürümleme: [Semantic Versioning 2.0.0](https://semver.org/lang/tr/)
- Yeni kayıtlar `[Unreleased]` altına yazılır; `scripts/release.ps1` sürüm çıkarken bunları sürüm başlığına taşır.
- Bölümler: `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`.

## [Unreleased]

### Added
- Solution (`.sln`, `.slnx`) ve proje (`.csproj`, `.fsproj`, `.vbproj`) ekleme; eklenen öğeler, favoriler ve tema kalıcı olarak hatırlanır.
- Proje keşfi: `appsettings*.json` ortamları, `launchSettings.json` ortamları, `UserSecretsId` kaynağı
  (csproj, `Directory.Build.props`, assembly attribute), user secrets desteği ve paylaşılan id tespiti.
- Solution özeti: her projenin ortamları, UserSecretsId'si, secret ve öneri sayısı.
- Anahtar × ortam matrisi; arama, filtreler, hassas anahtar önerileri ve değer maskeleme.
- Secret'a taşıma: çakışma çözümü, dosya bazlı temizleme seçimi, ortam uyarıları ve diff önizleme.
  Taşınan değerler appsettings'te `""` olarak kalır; yorumlar, girinti, BOM ve satır sonları korunur.
- Secret yönetimi: görüntüleme, ekleme, düzenleme, silme, kopyalama, sahipsiz secret tespiti ve appsettings'e geri taşıma.
- İşlem geçmişi ve yedekten geri yükleme; yazma hatasında otomatik geri alma.
- Dosya değişikliği izleme, açık/koyu/sistem teması, sürükle-bırak ile proje ekleme.
- `USERSECRETMANAGER_DATA_DIR` ile uygulama veri klasörünü değiştirme (taşınabilir kullanım).
