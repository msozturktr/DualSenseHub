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
- `dotnet run -- --ets2-listen-test [--seconds=3]`: UDP 47555'i dinleyip çözülen paketleri yazdırır.
- `dotnet run -- --apply-last [--wait=15]`: kayıtlı rengi arayüzsüz uygular ve çıkar. Animasyonlu efektler için ilk kare/anlık renk uygulanır.

## ETS2 (Euro Truck Simulator 2) modu

Oyundaki sinyal, dörtlü, fren, motor, hız/devir ve yakıt durumuna göre ışık çubuğu ve oyuncu LED'leri değişir.

1. Eklentiyi derleyin (clang + lld + llvm-dlltool gerekir, mingw gerekmez): `./ets2-plugin/build.sh`
2. Oyun klasörüne kurun: `./scripts/install-ets2-plugin.sh` (`bin/win_x64/plugins/` altına kopyalar; farklı yol için `ETS2_DIR=...`).
3. Oyunu başlatın; eklenti ilk açılışta onay sorabilir ("Advanced SDK features" uyarısı), onaylayın.
4. Uygulamada Oyun Modları sekmesinde "Euro Truck Simulator 2" seçip Başlat'a basın. Bağlantı "Bağlı" olunca ışıklar oyuna tepki verir.
5. Işıklar sürekli sıfırlanıyor veya titriyorsa Steam'de ETS2 için Steam Girdisi'ni (Steam Input) devre dışı bırakın.

Kurallar (öncelik sırasıyla): sinyal/dörtlü (turuncu, oyunla eşzamanlı yanıp söner, sol/sağ LED'ler) > fren (kırmızı) > motor kapalı (ışık kapalı) > düşük yakıt (<%15, dururken turuncu nabız) > hız veya devirle yeşil→kırmızı geçiş. Duraklatılınca ışık kısılır. Her kural açılıp kapatılabilir, renkler RRGGBB olarak düzenlenir; ayarlar `~/.config/dualsensehub/ets2.json` içindedir. Test için oyun olmadan `--ets2-sim` kullanın. Paket biçimi: `ets2-plugin/PROTOCOL.md`.

## Açılışta rengi geri yükleme

1. Derleyip yayınlayın: `dotnet publish -c Release -o ~/.local/share/dualsensehub` ve `ln -sf ~/.local/share/dualsensehub/DualSenseHub ~/.local/bin/dualsensehub`.
2. `cp packaging/dualsensehub-restore.service ~/.config/systemd/user/` ardından `systemctl --user enable --now dualsensehub-restore.service`.

Ayarlar ve profiller `~/.config/dualsensehub/` altında JSON olarak tutulur.

## Sınırlamalar

- Donanım sürümü ve yazılım sürümü ham sayı olarak gösterilir.
- ETS2 modu yalnızca Steam/Proton kurulumunda denendi; eklenti oyun içinde ilk kez yüklendiğinde `~/.local/share/Steam/steamapps/compatdata/227300/pfx/drive_c/users/steamuser/` altındaki oyun günlüğünden kontrol edilmelidir.
- Tetik efekt parametreleri topluluk belgelerine dayanır; farklı donanım sürümlerinde his değişebilir.
