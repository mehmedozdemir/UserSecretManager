# Changelog

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.

- Biçim: [Keep a Changelog 1.1.0](https://keepachangelog.com/tr/1.1.0/)
- Sürümleme: [Semantic Versioning 2.0.0](https://semver.org/lang/tr/)
- Yeni kayıtlar `[Unreleased]` altına yazılır; `scripts/release.ps1` sürüm çıkarken bunları sürüm başlığına taşır.
- Bölümler: `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`.

## [Unreleased]

## [0.3.0] - 2026-09-26

### Added
- Profil düzenleyici: profilin secret değerleri tabloda düzenlenir; anahtar eklenip çıkarılabilir, profil yeniden
  adlandırılabilir. Kaydetmek yalnızca profili değiştirir, `secrets.json` profil uygulanınca güncellenir.
- Yeni profil başlangıç değerlerini şu anki secret'lardan, bir ortamdan, **başka bir profilden** veya boş alabilir ve
  kaydetmeden önce düzenlenebilir. Tablo elle değiştirildiyse kaynak değişikliği tabloyu ezmez.

### Changed
- Profillerdeki ayrı "yeniden adlandır" butonu kaldırıldı; ad değişikliği düzenleme ekranından yapılır.

## [0.2.1] - 2026-09-26

### Fixed
- Profil uygulama penceresi profilin secret'larını göstermiyordu; profil `secrets.json` ile aynıysa yalnızca
  "değişiklik yok" yazıyor ve "Uygula" açıklamasız pasif kalıyordu. Pencere artık profildeki her secret'ı durumuyla
  (aynı / değişecek / eklenecek / silinecek / korunacak) listeliyor, dosya farkı ayrı sekmede. Profil zaten etkinse
  bu açıkça belirtiliyor. Aynı pencere içe aktarmada da kullanılıyor.

## [0.2.0] - 2026-09-25

### Added
- Projeye ek JSON yapılandırma dosyaları (`ocelot.json`, `serilog.json` vb.) eklenebilir. Bu dosyalar matriste ayrı
  sütun olarak görünür, tüm ortamlarda geçerli sayılır ve secret'a taşımaya katılır. Seçim proje bazında hatırlanır;
  bulunamayan dosyalar uyarı olarak gösterilir.
- "Etkin yapılandırma" sekmesi: seçilen ortam ve launch profili için uygulamanın göreceği son değerler, her değerin
  kaynağı ve ezdiği katmanlar. Development dışı ortamlarda boş kalan (ör. secret'a taşınmış) değerler ayrıca uyarılır.
- Secret profilleri: secret değerlerinin adlandırılmış, şifreli kopyaları (Windows'ta DPAPI, diğer sistemlerde yalnızca
  kullanıcının okuyabildiği anahtarla AES-256-GCM). Profil şu anki secret'lardan veya bir ortamın değerlerinden
  oluşturulur; diff önizlemesiyle "değiştir" ya da "birleştir" modunda `secrets.json`'a uygulanır ve geçmişe yedeklenir.
  secrets.json ile birebir aynı olan profil "Etkin" olarak işaretlenir.
- Parola korumalı dışa/içe aktarma (`.usmsecrets`, PBKDF2-SHA256 + AES-256-GCM). İçe aktarma diff önizlemesiyle
  "değiştir" veya "birleştir" modunda uygulanır; farklı bir UserSecretsId'den gelen dosya için uyarı verilir.
- `secrets.template.json`: değerleri boş, repoya eklenebilen anahtar listesi. Secrets sekmesinden oluşturulur ve
  şablondaki eksik anahtarlar tek tıkla forma eklenir.

### Fixed
- Seçilebilir metinlerde (anahtar adları, yollar) sabit genişlikli font ve ikincil metin stilleri uygulanmıyordu.

## [0.1.0] - 2026-09-25

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
