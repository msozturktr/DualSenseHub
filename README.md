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
- `dotnet run -- --apply-last [--wait=15]`: kayıtlı rengi arayüzsüz uygular ve çıkar. Animasyonlu efektler için ilk kare/anlık renk uygulanır.

## Açılışta rengi geri yükleme

1. Derleyip yayınlayın: `dotnet publish -c Release -o ~/.local/share/dualsensehub` ve `ln -sf ~/.local/share/dualsensehub/DualSenseHub ~/.local/bin/dualsensehub`.
2. `cp packaging/dualsensehub-restore.service ~/.config/systemd/user/` ardından `systemctl --user enable --now dualsensehub-restore.service`.

Ayarlar ve profiller `~/.config/dualsensehub/` altında JSON olarak tutulur.

## Sınırlamalar

- Donanım sürümü ve yazılım sürümü ham sayı olarak gösterilir.
- Oyun Modları sekmesi şimdilik boş; ETS2 desteği (SCS SDK, UDP) sonraki aşamada eklenecek.
- Tetik efekt parametreleri topluluk belgelerine dayanır; farklı donanım sürümlerinde his değişebilir.
