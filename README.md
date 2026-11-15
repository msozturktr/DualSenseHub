# DualSense Hub

Linux için DualSense (PS5) denetleyici yöneticisi: ışık çubuğu renk/efektleri, oyuncu ve mikrofon LED'leri, cihaz bilgisi, canlı giriş testi, titreşim ve uyarlanabilir tetik testi. C# + Avalonia UI, .NET 10.

## Kurulum

1. Gereksinimler: .NET 10 SDK, `hid_playstation` çekirdek sürücüsü (modern çekirdeklerde hazır).
2. Erişim izni (bir kez, sudo gerekir):
   ```
   ./scripts/install-udev.sh
   ```
   Ardından denetleyiciyi çıkarıp yeniden bağlayın. İzin yoksa uygulama üstte kırmızı bir uyarı gösterir.
3. Çalıştırma:
   ```
   dotnet run
   ```

## Komut satırı

- `dotnet run -- --info`: bulunan cihazın yollarını, bataryayı ve (hidraw okunabiliyorsa) yazılım bilgisini yazdırır.
- `dotnet run -- --set-color RRGGBB`: ışık çubuğunu verilen renge ayarlar ve çıkar.
- `dotnet run -- --ets2-sim [--seconds=15]`: oyun olmadan sahte ETS2 paketleri gönderir (boşta, sol, sağ, dörtlü, fren döngüsü).
- `dotnet run -- --ets2-haptics-test [--seconds=30]`: sahte paketleri doğrudan titreşim/tetik hesabından geçirip özet yazdırır (denetleyici ve UDP gerekmez).
- `dotnet run -- --rumble-test`: gerçek denetleyicide ~200 ms'lik tek bir titreşim gönderir ve yazmanın başarılı olup olmadığını bildirir.
- `dotnet run -- --ets2-listen-test [--seconds=3]`: UDP 47555'i dinleyip çözülen paketleri yazdırır.
- `dotnet run -- --apply-last [--wait=15]`: kayıtlı rengi arayüzsüz uygular ve çıkar. Animasyonlu efektler için ilk kare/anlık renk uygulanır.

## ETS2 (Euro Truck Simulator 2) modu

Oyundaki sinyal, dörtlü, fren, motor, hız/devir ve yakıt durumuna göre ışık çubuğu ve oyuncu LED'leri değişir.

1. Eklentiyi derleyin (clang + lld + llvm-dlltool gerekir, mingw gerekmez): `./ets2-plugin/build.sh`
2. Oyun klasörüne kurun: `./scripts/install-ets2-plugin.sh` (`bin/win_x64/plugins/` altına kopyalar; farklı yol için `ETS2_DIR=...`).
3. Oyunu başlatın; eklenti ilk açılışta onay sorabilir ("Advanced SDK features" uyarısı), onaylayın.
4. Uygulamada Oyun Modları sekmesinde "Euro Truck Simulator 2" seçip Başlat'a basın. Bağlantı "Bağlı" olunca ışıklar oyuna tepki verir.
5. Işıklar sürekli sıfırlanıyor veya titriyorsa Steam'de ETS2 için Steam Girdisi'ni (Steam Input) devre dışı bırakın.

Kurallar (öncelik sırasıyla): sinyal/dörtlü (turuncu, oyunla eşzamanlı yanıp söner, sol/sağ LED'ler) > fren (kırmızı) > motor kapalı (ışık kapalı) > düşük yakıt (<%15, dururken turuncu nabız) > hız veya devirle yeşil→kırmızı geçiş. Duraklatılınca ışık kısılır. Her kural açılıp kapatılabilir, renkler RRGGBB olarak düzenlenir; ayarlar `~/.config/dualsensehub/ets2.json` içindedir. Test için oyun olmadan `--ets2-sim` kullanın (artık vites değişimi, tümsek, iniş, fren kilitlenmesi ve devir taraması da içerir).

### Titreşim ve tetikler

Oyun Modları sekmesindeki "Titreşim ve tetikler" bölümünde her özellik ayrı açılıp kapatılır ve %0-100 şiddet kaydırıcısı vardır (ayarlar `ets2.json` içinde):

- **Yol hissi**: tekerlek süspansiyon hızındaki ani değişimler kısa titreşim darbesi olur (sol tekerlekler sol/güçlü motor, sağ tekerlekler sağ/zayıf motor); havadan yere iniş de darbe verir. Düz yolda ölü bölge sayesinde titreşim yoktur.
- **Vites geçişi**: vites değişince ~80 ms'lik kısa vuruş (nötr geçişleri sayılmaz).
- **Tetikler**: L2 fren pedalına göre direnç kazanır, el freni çekiliyken sert/kilitli hissi verir; R2 devir sınırına (%85 üstü) yaklaştıkça sertleşir. Efekt yalnızca değiştiğinde gönderilir.
- **ABS / tekerlek kilitlenmesi**: fren sırasında bir tekerleğin yüzey hızı araç hızından çok düşükse L2 titrer (hafif titreşim de eklenir). SDK'da ABS kanalı olmadığı için kilitlenme tekerlek hızından çıkarılır.
- **Motor uğultusu**: motor açıkken devirle orantılı hafif düşük titreşim; varsayılan kapalı.

Duraklatma, bağlantı kopması veya mod durması titreşimi sıfırlar ve tetikleri serbest bırakır. Titreşim en fazla ~30 Hz güncellenir, yalnızca değer değişince (veya sıfırdan farklıysa 500 ms'de bir) gönderilir. Eklentiyi güncelledikten sonra `./scripts/install-ets2-plugin.sh` ile yeniden kurun (paket sürümü 2). Not: ETS2'nin kendi kontrolcü titreşimi bununla çakışabilir; oyunun kontrol seçeneklerinden "Force feedback / titreşim" ayarını kapatın. Tetik ve titreşim çıktıları henüz gerçek donanımda ve oyunda denenmedi. Paket biçimi: `ets2-plugin/PROTOCOL.md`.

## Açılışta rengi geri yükleme

1. Derleyip yayınlayın: `dotnet publish -c Release -o ~/.local/share/dualsensehub` ve `ln -sf ~/.local/share/dualsensehub/DualSenseHub ~/.local/bin/dualsensehub`.
2. `cp packaging/dualsensehub-restore.service ~/.config/systemd/user/` ardından `systemctl --user enable --now dualsensehub-restore.service`.

Ayarlar ve profiller `~/.config/dualsensehub/` altında JSON olarak tutulur.

## Sınırlamalar

- Donanım sürümü ve yazılım sürümü ham sayı olarak gösterilir.
- ETS2 modu henüz gerçek oyunda denenmedi; ilk açılışta eklentinin yüklendiğini oyunun konsol/günlük çıktısından doğrulayın.
- Tetik efekt parametreleri topluluk belgelerine dayanır; farklı donanım sürümlerinde his değişebilir.
