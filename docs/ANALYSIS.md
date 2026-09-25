# UserSecretManager — Analiz ve Tasarım

> Durum: v1 yayında (0.1.0), v2 tamamlandı · Son güncelleme: 2026-09-25

## 1. Amaç

.NET projelerindeki `appsettings*.json` dosyalarında duran hassas değerleri (connection string, parola, API anahtarı vb.)
**kullanıcının seçtiği** anahtarlar üzerinden `dotnet user-secrets` deposuna taşıyan, appsettings'ten temizleyen ve
secret'ları daha sonra okuyup yönetebilen, çapraz platform bir masaüstü uygulaması.

## 2. Temel teknik gerçekler

| Konu | Gerçek | Tasarıma etkisi |
|---|---|---|
| Depo | Proje başına tek `UserSecretsId` → tek `secrets.json` | Ortam başına ayrı secret yoktur |
| Konum | Windows: `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`<br>Linux/macOS: `~/.microsoft/usersecrets/<id>/secrets.json` | Dosyaya doğrudan erişilir, `dotnet` CLI gerekmez |
| Format | Düz (flat) anahtarlar: `Section:Sub:Key`, diziler `Items:0` | İç içe JSON ↔ düz anahtar dönüşümü |
| Yükleme | Varsayılan host yalnızca `Development` ortamında user secrets yükler | Development dışı dosyalar için uyarı zorunlu |
| Öncelik | `appsettings.json` → `appsettings.{Env}.json` → **user secrets** → env vars → CLI | Development'ta etkin değer = secret > Dev dosyası > base |
| ID tanımı | `.csproj`, `Directory.Build.props` veya `[assembly: UserSecretsId]` | Üç kaynak da taranır; yoksa csproj'a eklenir |
| Destek | Web SDK otomatik; Console/Worker/lib için paket + `AddUserSecrets` gerekir | Proje tipine göre bilgi uyarısı |

## 3. Kararlar

| # | Karar | Gerekçe |
|---|---|---|
| K1 | **Avalonia 12 + .NET 10**, sade/işlevsel "IT profesyonel" arayüz | Çapraz platform, olgun MVVM |
| K2 | Taşınan değer appsettings'te **boş string (`""`)** olarak kalır | Yapı belgelenmiş kalır, options binding bozulmaz |
| K3 | Varsayılan secret değeri = **Development'ta etkin olan değer** (mevcut secret > `appsettings.Development.json` > `appsettings.json` > diğer ortamlar) | User secrets yalnızca Development'ta okunur; etkin değeri korumak davranışı değiştirmez |
| K4 | Aynı anahtar dosyalarda **farklı** değerlere sahipse **çakışma** oluşur; kullanıcı hangi değerin secret olacağını seçer | Sessiz veri kaybını önler |
| K5 | Development dışı ortam dosyasından (ör. Production) temizleme **varsayılan olarak kapalı** gelir ve uyarı gösterilir; kullanıcı açıkça işaretlerse yapılır. Uyarı, ilgili ortam değişkeni adını (`ConnectionStrings__Default`) önerir | User secrets Production'da yüklenmez; temizlemek o ortamı bozar |
| K6 | JSON dosyaları **format korunarak** düzenlenir (yorumlar, girinti, BOM, satır sonları) | `System.Text.Json` ile yeniden yazmak yorumları siler |
| K7 | Her işlem öncesi **yedek** + işlem geçmişi; geçmişten geri yükleme | Geri alınabilirlik |
| K8 | Uygulama hafızası (`workspace.json`) **asla secret değeri tutmaz** | Güvenlik |
| K9 | `secrets.json` doğrudan okunur/yazılır, `dotnet` CLI çağrılmaz | Hız, SDK bağımsızlığı |
| K10 | Ek JSON dosyaları tüm ortamlarda geçerli ve **varsayılan kaynaklardan sonra** yüklenmiş kabul edilir | `builder.Configuration.AddJsonFile(...)` tipik olarak varsayılanlardan sonra çağrılır; gerçek sıra `Program.cs`'e bağlıdır ve arayüzde belirtilir |
| K11 | Etkin yapılandırma sırası: `appsettings.json` → `appsettings.{Ortam}.json` → user secrets → launch profili ortam değişkenleri (`__` → `:`) → ek dosyalar | Varsayılan host sırası; user secrets Development dışında varsayılan olarak kapalı |
| K12 | Profiller UserSecretsId başına tutulur ve kullanıcıya bağlı şifrelenir (Windows: DPAPI, diğer: kullanıcıya özel anahtar dosyası + AES-256-GCM). Anahtar adları düz metin, değerler şifreli | Secret'lar id başına; profil adları ve anahtar listesi hassas değil, listeleme için gerekli |
| K13 | Profil/içe aktarma uygulanırken, kaynak mevcut anahtarların hepsini içermiyorsa varsayılan mod **birleştir** | "Değiştir" eksik anahtarları siler; sessiz veri kaybı önlenir |
| K14 | Dışa aktarım: PBKDF2-SHA256 (600 000 iterasyon) + AES-256-GCM, başlık AAD olarak bağlı; dosyadaki iterasyon sayısı üst sınırlı | Parola ile taşınabilir; kurcalama ve DoS'a karşı dayanıklı |

## 4. Kapsam

### v1 (0.1.0)
- Solution (`.sln`, `.slnx`) veya proje (`.csproj`) ekleme, uygulama hafızasında tutma (favori, son açılan, bulunamayan proje tespiti)
- Proje keşfi: `appsettings*.json`, `launchSettings.json` ortamları, `UserSecretsId` kaynağı, user secrets desteği kontrolü, paylaşılan ID tespiti
- Anahtar × ortam matrisi; arama, filtre, hassas anahtar önerileri (ad ve değer kalıpları)
- Taşıma sihirbazı: çakışma çözümü, dosya bazlı temizleme seçimi, uyarılar, **diff önizleme**, uygulama
- Secret yönetimi: listele, maskele/göster, kopyala, ekle, düzenle, sil, appsettings'e geri taşı, sahipsiz secret tespiti
- İşlem geçmişi ve yedekten geri yükleme
- Dosya değişikliği izleme (dışarıdan düzenlemede yenileme uyarısı)
- Git reposu uyarısı (değer git geçmişinde kalır → rotate önerisi)

### v2 (tamamlandı)
- Etkin yapılandırma görünümü: ortam + launch profili seçimi, değer kaynağı, ezilen katmanlar, boş değer uyarısı
- Secret profilleri: şifreli, adlandırılmış setler; şu anki secret'lardan veya bir ortamdan oluşturma; değiştir/birleştir
- Parola korumalı dışa/içe aktarma, `secrets.template.json` üretimi ve şablondan eksik anahtarları tamamlama
- Ek config dosyaları (ör. `ocelot.json`) ekleme

### Sonraki adaylar
- CLI (`usm migrate`, `usm export`) — Core katmanı hazır
- Azure Key Vault / AWS Secrets Manager'a aktarım (Production değerleri için)
- `Program.cs` analiziyle ek dosyaların gerçek yüklenme sırasını tespit

## 5. Mimari

```
UserSecretManager.Core   (UI bağımsız, test edilir)
├── Configuration   JSONC okuma, düzleştirme, format koruyan editör, satır diff
├── Discovery       .sln/.slnx okuma, proje inceleme, UserSecretsId çözümü
├── Secrets         secrets.json yolu, okuma/yazma, UserSecretsId ekleme
├── Analysis        hassas anahtar önerileri
├── Effective       ortam bazında etkin yapılandırma hesaplama
├── Profiles        şifreli secret profilleri
├── Security        kullanıcıya bağlı şifreleme (DPAPI / anahtar dosyası)
├── Transfer        parolalı dışa/içe aktarma, secrets.template.json
├── Migration       plan oluşturma (çakışma/uyarı), uygulama, geri taşıma
├── History         yedek + işlem geçmişi + geri yükleme
├── Workspace       kalıcı proje listesi ve tercihler
└── IO              atomik dosya yazma, uygulama klasörleri

UserSecretManager.App    (Avalonia, MVVM — CommunityToolkit.Mvvm)
UserSecretManager.Core.Tests (xUnit v3)
```

### Taşıma akışı
1. Kullanıcı matris üzerinde anahtarları seçer (öneriler vurgulu).
2. `MigrationPlanner` her anahtar için aday değerleri toplar, varsayılanı K3'e göre belirler, çakışma ve uyarıları üretir.
3. İnceleme penceresi: çakışmalar, secret listesi, dosya bazlı temizleme işaretleri, diff önizleme.
4. `MigrationExecutor`: yedek al → (gerekirse) `UserSecretsId` ekle → `secrets.json` yaz → JSON dosyalarını düzenle. Hata olursa yedekten geri döner.

## 6. Güvenlik notları
- Yedekler kullanıcı profilinde (`LocalApplicationData/UserSecretManager/backups`) düz metin tutulur; `secrets.json` ile aynı güven seviyesindedir. Son 50 işlem saklanır.
- Uygulama hiçbir değeri loglamaz, ağ erişimi yoktur.
- Profiller başka bir kullanıcı veya makinede açılamaz (DPAPI / yerel anahtar). Taşımak için dışa aktarma kullanılır.
- Dışa aktarım dosyası parolayla korunur; parola dosyayla aynı kanaldan gönderilmemelidir.
- Taşınan değerler git geçmişinde kalır; uygulama repo tespit ettiğinde değerlerin değiştirilmesini (rotate) önerir.

## 7. Riskler
| Risk | Önlem |
|---|---|
| JSON bozulması | Format koruyan editör + yazma sonrası doğrulama (parse) + yedek |
| Yanlış ortamı bozma | K5 varsayılanları, uyarılar, diff önizleme |
| Paylaşılan UserSecretsId | Başlıkta uyarı; secret değişikliği tüm paylaşan projeleri etkiler |
| `$(Property)` ile tanımlı ID | Çözümlenemez olarak işaretlenir, işlem engellenir |
