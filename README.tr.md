# DualSense Hub

[English](README.md)

Sony DualSense (PS5) kontrolcüsü için bir Linux kontrol merkezi, Euro Truck Simulator 2 oyun modu ile birlikte.

- **Lightbar:** herhangi bir renk, parlaklık, ön ayarlar, profiller ve efektler (sabit, nefes, gökkuşağı, yanıp sönme, pil seviyesi rengi)
- **Oyuncu ve mikrofon LED'leri**
- **Cihaz bilgisi:** pil, bağlantı türü (USB/Bluetooth), MAC, firmware/donanım sürümleri
- **Canlı giriş testi:** düğmeler, çubuklar, tetikler, gyro/akseleratör, dokunmatik yüzey; titreşim ve adaptif tetik testleri
- **ETS2 modu:** dönüş sinyalleri ışık çubuğunu sarı yakıp söndürür ve ilgili tarafın oyuncu LED'lerini yakar, fren kırmızı olur, renk gaz pedalına ne kadar bastığınıza göre değişir, ayrıca dokunsal geri bildirim: yol hissi, vites geçiş darbesi, fren tetik direnci, gaz tetikinde kickdown tıkı, ABS/tekerlek kilidi titreşimi ve isteğe bağlı motor titreşimi
- **Sistem tepsisi** uygulaması otomatik başlatma ile; ETS2 modu oyun algılandığında otomatik olarak başlar

C# + Avalonia UI (.NET 10) ile inşa edilmiştir. Kontrolcüye kernel `hid_playstation` sürücüsü (sysfs) ve ham hidraw çıkış raporları (USB ve Bluetooth, CRC32 ile) üzerinden bağlanır.

## Kurulum

Gereksinimler: .NET 10 SDK, `hid_playstation` destekli bir kernel (herhangi bir modern dağıtım).

```bash
git clone https://github.com/msozturktr/DualSenseHub.git
cd DualSenseHub
./scripts/install-udev.sh   # bir kez, sudo gerekir: kullanıcınızın kontrolcüye erişmesini sağlar
./scripts/install.sh        # kullanıcı düzeyi: ~/.local/share/dualsensehub içine yayınlar, menü girişi + otomatik başlatma ekler
```

Udev kuralını kurduktan sonra kontrolcüyü yeniden bağlayın. Ardından **DualSense Hub**'ı uygulama menünüzden başlatın (ya da `dualsensehub` komutunu çalıştırın). Pencereyi kapatmak uygulamayı tepsiye bırakır; çıkmak için tepsi menüsündeki **Çıkış** seçeneğini kullanın. `install.sh` dosyasını yeniden çalıştırmak uygulamayı günceller ve çalışan örneği yeniden başlatır. `./scripts/uninstall.sh` kaldırır ( `~/.config/dualsensehub/` içindeki ayarlar korunur).

## ETS2 modu

ETS2 verileri, `127.0.0.1:47555` adresine UDP paketleri gönderen küçük bir SCS Telemetry SDK eklentisinden gelir. Bu eklenti Windows DLL'i olarak derlenir (oyunun Proton sürümüyle çalışır) ve clang + lld + llvm-dlltool kullanılarak oluşturulur, MinGW gerekmez.

```bash
./scripts/install-ets2-plugin.sh   # gerekirse derler ve DLL'i oyunun bin/win_x64/plugins/ klasörüne kopyalar
```

Oyun varsayılan Steam kütüphanesinde değilse `ETS2_DIR=...` ayarlayın. Oyunu başlatın ve "advanced SDK features" uyarısını kabul edin. Uygulama oyunu algılar ve modu otomatik olarak başlatır (**Oyun Modları** sekmesinde geçiş yapılabilir). Her kural ve dokunsal efekt açılıp kapatılabilir ve gücü burada ayarlanabilir. Paket formatı: [ets2-plugin/PROTOCOL.md](ets2-plugin/PROTOCOL.md).

Eklentiyi güncelledikten sonra oyunu yeniden başlatın.

## Steam

Steam çalışırken kontrolcüyü ele geçirip ışık çubuğunu geçersiz kılabilir. Renkler kalıcı olmazsa PlayStation kontrolcüleri için Steam Input'u devre dışı bırakın (veya oyunun kontrolcü ayarlarında ETS2 için). ETS2'nin kendi kontrolcü titreşimi de dokunsal geri bildirimle çakışabilir; oyunun ayarlarından bunu kapatın.

## Komut satırı

| Komut | Açıklama |
|---|---|
| `dualsensehub --status` | Çalışan örneğin durumu (UDP dinleyicisi, paket sayısı, bağlantı, mod) |
| `dualsensehub --info` | Algılanan cihaz yolları, pil, firmware |
| `dualsensehub --set-color RRGGBB` | Işık çubuğunu ayarla ve çık |
| `dualsensehub --apply-last` | UI olmadan kaydedilen rengi uygula |
| `dualsensehub --tray` | Gizli olarak tepside başlat |
| `dualsensehub --quit` | Çalışan örneği sonlandır |
| `--ets2-sim`, `--ets2-listen-test`, `--ets2-haptics-test`, `--rumble-test` | Geliştirme/test yardımcıları |

Kaynak ağacından `dotnet run -- <args>` komutunu kullanın.

## Notlar

- Lightbar kontrolü: kontrolcü, bir kez yapılan "lightbar setup" (`LIGHT_OUT`) komutundan sonra sadece host renklerini kabul eder; uygulama bunu her bağlantıda bir kez gönderir.
- Adaptif tetik parametreleri topluluk belgelerine dayanır ve donanım revizyonlarına göre farklı hissettirebilir.
- CachyOS (KDE) üzerinde Bluetooth üzerinden DualSense ile test edilmiştir.

## Lisans

MIT, bkz. [LICENSE](LICENSE). ETS2 eklentisi, SCS Telemetry SDK ile uyumlu el yazısı bildirimler içerir; bkz. `ets2-plugin/sdk/SCS_SDK_LICENSE.txt`.

Sony Interactive Entertainment veya SCS Software tarafından desteklenmez veya onaylanmaz. "DualSense" ve "PlayStation" terimleri Sony Interactive Entertainment'in ticari markalarıdır.