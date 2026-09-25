# ADR 0001: Avalonia UI ve secrets.json'a doğrudan erişim

- Durum: Kabul edildi
- Tarih: 2026-09-25

## Bağlam

Uygulama farklı işletim sistemlerinde çalışan .NET geliştiricileri için bir masaüstü aracı. Secret'lar
`dotnet user-secrets` ile aynı yerde ve aynı formatta tutulmalı.

## Karar

1. UI için **Avalonia 12** (Fluent tema, MVVM, CommunityToolkit.Mvvm) kullanılır.
2. `secrets.json` **doğrudan** okunup yazılır; `dotnet user-secrets` CLI çağrılmaz. Yol çözümü
   `Microsoft.Extensions.Configuration.UserSecrets.PathHelper` ile aynıdır.
3. JSON dosyaları token konumları üzerinden **metin olarak** düzenlenir; yeniden serileştirilmez.
4. İş mantığı UI'dan bağımsız `UserSecretManager.Core` kütüphanesindedir.

## Alternatifler

| Seçenek | Neden seçilmedi |
|---|---|
| WPF / WinUI 3 | Yalnızca Windows'ta çalışır |
| `dotnet user-secrets` CLI | SDK gerektirir, yavaş, toplu işlem ve geri alma yok |
| `JsonNode` ile yeniden yazma | Yorumları ve biçimi kaybeder |

## Sonuçlar

- Çekirdek, ileride bir CLI veya IDE eklentisi tarafından yeniden kullanılabilir.
- `secrets.json` formatında değişiklik olursa yol ve format mantığı tek yerde (`UserSecretsStore`) güncellenir.
