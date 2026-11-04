# 🎬 Catatan Panduan Pembuatan Konten: Keystream Master Walkthrough

> **Catatan Creator:** Dokumen ini disusun sebagai panduan langkah demi langkah (*script & recording checklist*) untuk membuat video ulasan, panduan lengkap, atau artikel tutorial seputar aplikasi **Keystream**.

---

## 📌 Ringkasan Konsep Utama (Kunci Pesan / Value Proposition)
* **Apa itu Keystream?** Aplikasi Windows gratis dan open-source yang mengubah keyboard biasa/bekas (USB/Bluetooth/Numpad) menjadi papan kontrol pintar (*Stream Deck alternatif*).
* **Keunggulan Utama:** 
  1. **Isolasi Hardware Total:** Keyboard utama tetap normal untuk mengetik; hanya keyboard kedua yang dicegat oleh driver.
  2. **100% Gratis & Open-Source (MIT):** Tidak perlu membeli alat fisik seharga jutaan rupiah (seperti Elgato Stream Deck).
  3. **Privasi & Offline:** Tidak butuh akun, tidak ada cloud, tanpa telemetri mata-mata.
  4. **Failsafe (Panic Key):** Tekan `F9` atau `Ctrl+Shift+Esc` untuk menonaktifkan mode deck seketika.

---

## 🧭 Alur Penelusuran Step-by-Step (Urutan Demo Konten)

### Tahap 1: Pembuka, Hook & Perbandingan (00:00 - 02:00)
- [ ] **Buka dengan perbandingan fisik:** Tunjukkan Elgato Stream Deck (atau foto harganya di e-commerce seharga Rp 1,5 - 4 juta) berdampingan dengan keyboard bekas/numpad murah.
- [ ] **Poin Bahasan:**
  * *"Daripada beli alat jutaan, kenapa tidak manfaatkan keyboard nganggur di rumah?"*
  * Jelaskan bagaimana Windows biasanya memperlakukan 2 keyboard sebagai 1 kesatuan (keduanya mengetik huruf yang sama).
  * Keystream memecahkan masalah ini dengan **Interception Driver** — keyboard kedua dicegat di tingkat kernel Windows sehingga tidak mengganggu ketikan di keyboard utama.
- [ ] **Demonstrasi Panic Key:** Tekan `F9` untuk memperlihatkan keyboard kedua langsung kembali mengetik normal tanpa harus mencabut kabel.

---

### Tahap 2: Instalasi Cepat & Smart Pairing (02:00 - 04:00)
- [ ] **Demonstrasi Installer (`KeystreamSetup.exe`):**
  * Tunjukkan jendela setup bertema *cyber dark*.
  * Sorot opsi baru: **`Run at Start Up`** (default tidak tercentang) dan **`Launch Keystream now`**.
  * Jelaskan perlunya restart komputer 1 kali setelah install agar driver aktif.
- [ ] **Smart Pairing Wizard:**
  * Buka aplikasi pertama kali.
  * Tunjukkan wizard: *"Cukup tekan 1 tombol sembarang di keyboard kedua."*
  * Begitu tombol ditekan, Hardware ID langsung terkunci otomatis. Selesai dalam 3 detik!

---

### Tahap 3: Tur Tampilan UI & Navigasi (04:00 - 06:00)
- [ ] **Header Bar (Atas):**
  * Indikator status koneksi deck (Hijau = Aktif & Terhubung).
  * Tombol jeda/passthrough (Pause).
  * Pemilih bahasa instan: **ID** (Indonesia) / **EN** (English).
  * Tombol *Mode Pilih* (Bulk Select) untuk edit/hapus banyak kartu sekaligus.
  * Tombol Settings.
- [ ] **Tampilan Grid vs List View:**
  * Beralih antara tampilan ubin visual (*Grid View*) dan tampilan daftar baris (*List View*).
- [ ] **Interaksi Kartu Visual:**
  * Tunjukkan kartu yang menyala (*flash*) secara *real-time* saat tombol fisik di keyboard ditekan.
  * Tunjukkan status kartu nonaktif (*dimmed*) dan lencana peringatan konflik tombol (ikon ⚠).

---

### Tahap 4: Sistem Profil, Halaman & Global Page (06:00 - 09:00)
- [ ] **Struktur Profil & Halaman:**
  * 1 Profil bisa berisi banyak Halaman (Pages).
  * Cocok untuk membuat profil terpisah: *OBS Streaming*, *Gaming*, *Editing Premiere/Photoshop*, *Kerja Harian*.
- [ ] **Sorotan Khusus: Halaman Global (Global Page):**
  * Jelaskan fungsi Global Page: tombol yang ditaruh di sini **selalu aktif di profil mana pun**.
  * Contoh terbaik: Tombol Mic Mute, Master Volume, dan Tombol Pindah Halaman (*Next/Prev Page*).
- [ ] **Fitur Palet Warna Cepat (Quick Palette):**
  * Buka edit halaman $\rightarrow$ klik salah satu dari 8 swatch warna.
  * Seluruh kartu dalam satu halaman langsung berubah tema warna seketika.
- [ ] **Ekspor & Impor Paket Profil (`.ksp`):**
  * Tunjukkan fitur simpan/bagikan profil dengan opsi penanganan kartu global (*Replace*, *Merge*, atau *Skip*).

---

### Tahap 5: 7 Mode Pemicu Tombol / Trigger (09:00 - 13:00)
> Buka Editor Kartu dan tunjukkan variasi cara menekan tombol:

| Mode Trigger | Cara Kerja | Contoh Penggunaan Terbaik |
| :--- | :--- | :--- |
| **Tap** | Tekan sekali, langsung jalan. | Buka aplikasi, shortcut standar (Ctrl+C). |
| **Long Press** | Tahan selama durasi tertentu (misal 500ms). | Perintah sensitif: *Stop Stream*, *Reboot PC*. |
| **Double Tap** | Tekan 2 kali cepat. | Ganti scene darurat, refresh browser. |
| **Hold to Repeat** | Tahan untuk mengulang terus-menerus. | Volume turun cepat, navigasi panah. |
| **Hold for Layer** | Tahan untuk membuka halaman lain sementara. | Numpad layer tambahan, menu shortcut sementara. |
| **Scroll Dial** | Tahan tombol deck + putar scroll mouse. | **Master Volume Dial** (suara senyap, tanpa spam klik!). |
| **Toggle Mode** | Tekan bergantian antara Status A dan Status B. | Lampu studio ON/OFF, mute/unmute. |
| **Kombinasi (Chords)** | Tahan Tombol A + tekan Tombol B. | Kombo shortcut tingkat lanjut tanpa tombol fisik ekstra. |

---

### Tahap 6: Bedah Katalog 12+ Tipe Aksi (13:00 - 20:00)
> Tunjukkan fleksibilitas dropdown "Tipe Aksi":

1. **Hotkey:** Kirim tombol apa saja (termasuk F13 - F24 yang tidak ada di keyboard standar).
2. **Ketik Teks (Text):** Otomatis mengetik template teks panjang / format email dalam 1 detik.
3. **Media & Volume:** Play/Pause, Next/Prev Track, Volume Up/Down.
   * **Fitur Keren:** Tombol **Mic Mute** memiliki garis merah di kartu jika mikrofon Windows sedang termute.
4. **Integrasi OBS Studio:**
   * Sambungkan ke OBS via obs-websocket v5 bawaan.
   * Pindah scene langsung, sembunyikan/tampilkan sumber (webcam, game capture), mulai/stop stream/record.
5. **Integrasi Streamer.bot:** Jalankan aksi bot live streaming interaktif untuk penonton.
6. **Integrasi Steam:**
   * Otomatis membaca game Steam yang terinstall di PC secara offline.
   * Otomatis mengambil ikon resmi game ke dalam kartu.
   * Tombol pintas: Toggle Downloads, Toggle Friends List, dan Big Picture Mode (Toggle/Masuk/Keluar).
   * Opsi *Saat Game Sedang Berjalan* untuk auto-navigasi.
7. **Buka Aplikasi (Launcher):** Luncurkan file `.exe` apa pun dengan parameter kustom.
8. **Buka Link Web (URL):** Masukkan alamat web apa saja; favicon resolusi tinggi otomatis terunduh.
9. **Soundboard (Sound):** Mainkan file efek audio (.wav / .mp3) langsung saat tombol dipencet.
10. **Browser Profile & Groups:** Buka profil Chrome/Edge tertentu, atau luncurkan 5 profil riset sekaligus dalam 1 tombol.
11. **Perintah Windows:** Task Manager, Buka Virtual Desktop Baru, Geser Desktop Kiri/Kanan, Lock Windows.
12. **Macro Sequencer:** Rekam pergerakan mouse, klik, ketikan tombol, dan jeda waktu presisi.

---

### Tahap 7: Otomasi Canggih (Auto-Switch & Switch-Back) (20:00 - 23:00)
- [ ] **Auto-Switching:**
  * Hubungkan profil dengan aplikasi (misal: kaitkan profil dengan `obs64.exe` atau file game).
  * Demo: Buka OBS $\rightarrow$ deck otomatis berpindah ke profil OBS.
- [ ] **Switch-Back Cerdas:**
  * Demo: Buka aplikasi lain $\rightarrow$ deck otomatis kembali ke profil kerja/utama sebelumnya tanpa perlu tombol manual.

---

### Tahap 8: Pengaturan, Keamanan & Penutup (23:00 - Selesai)
- [ ] **Pengaturan Aplikasi:**
  * Pilihan bahasa (Indonesia / English).
  * Opsi *Run at Startup*, *Start hidden in tray*, dan *Always on top*.
  * Feedback suara mekanik (*Sound on Press*) yang responsif dan bebas spam.
  * Pemeriksaan update otomatis via GitHub Releases.
- [ ] **Keamanan & Uninstaller:**
  * Tunjukkan jendela uninstaller: opsi hapus profil dan uninstall driver secara default **tidak dicentang**, sehingga aman jika hanya ingin reinstall.
- [ ] **Call to Action (CTA):**
  * Tautkan link unduhan GitHub di deskripsi.
  * Ajak penonton memberi bintang (Star) di repo GitHub Keystream.
  * Tanya penonton: *"Kalian bakal pakai keyboard bekas apa buat dijadiin deck?"*

---

## 💡 Ide Judul & Konsep Thumbnail Video

### Rekomendasi Judul:
1. **"GAK PERLU BELI STREAM DECK! Ubah Keyboard Bekas Jadi Macro Deck Canggih (100% Gratis!)"**
2. **"Tutorial Lengkap Keystream: Alternatif Elgato Stream Deck Paling Worth It di Windows"**
3. **"Punya Keyboard Nganggur? Sulap Jadi Alat Streaming & Produktivitas Mewah!"**

### Konsep Visual Thumbnail:
* **Sisi Kiri:** Foto Elgato Stream Deck dengan teks harga dicoret: `~~Rp 2.500.000~~ ❌`
* **Sisi Kanan:** Foto keyboard biasa/numpad dengan panah menyala mengarah ke aplikasi Keystream: `GRATIS 100% ✅`
* **Teks Thumbnail:** *"STREAM DECK DARI KEYBOARD BEKAS?!"*

