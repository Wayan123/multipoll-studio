# MultiPoll Studio Android — rencana implementasi BMAD

Status: pilot Android, baseline desktop [main @ 027fb6e](https://github.com/Wayan123/multipoll-studio/tree/027fb6e015aa5ba45610668cf2a48aac2074e1f6). Target: master diagnostik Modbus RTU serial generik, sejenis modpoll, untuk sensor, inverter/VFD, meter dan PLC yang bertindak sebagai server RTU. APK debug tersedia; kualifikasi industri pada perangkat fisik belum selesai.

## Discovery: apa yang dapat dipakai kembali?

| Bukti | Implikasi untuk Android |
|---|---|
| `openpoll-win/src/Core/Transport.cs` menggunakan `System.IO.Ports`, nama COM, dan opsi RTS/echo. Windows menggunakan WinForms/.NET Framework 4.8. | Jangan port form atau membuka `COM1` di HP. Pertahankan desktop sebagai pembanding hasil, bukan dependensi runtime. |
| `openpoll/tests/test_core.py` menguji frame `01 03 00 00 00 0A C5 CD`, CRC rusak, unit salah, dan exception `83 02`. | Jadikan byte serta hasil yang diharapkan sebagai fixture lintas edisi. Jangan jadikan respons aplikasi Android sendiri sebagai oracle. |
| `openpoll-win/profiles/` menyimpan profil JSON; README membedakan fitur Windows dan Python. | Evaluasi impor profil setelah RTU stabil; jangan menjanjikan profil, workspace, scan, atau paritas semua fungsi saat rilis awal. |

## Riset koneksi dan keputusan arsitektur

**Jalur yang diuji:** HP USB-C sebagai *host* → adaptor OTG USB-C ke USB-A betina (data, USB 2.0 atau 3.0) → dongle USB–RS485 → pasangan diferensial RS485 → perangkat Modbus dengan catu sendiri. OTG dan konverter RS485 adalah dua fungsi berbeda. USB 3.0 bukan syarat untuk 9600 baud. Bentuk USB-C maupun warna konektor bukan bukti dukungan host/data. Cocokkan polaritas menurut manual kedua ujung; gunakan bus uji dengan satu master saja. Untuk instalasi industri, tinjau isolasi galvanik, referensi sinyal, bias dan terminasi sesuai topologi sebelum menyambungkan HP; jangan menyuplai peralatan lapangan dari HP.

- [Android USB Host](https://developer.android.com/develop/connectivity/usb/host): host mengenumerasi dan menyuplai bus; tidak semua HP mendukung fitur ini. Deklarasikan `android.hardware.usb.host`, enumerasi juga jika dongle sudah tertancap sebelum aplikasi dibuka. Pilih perangkat dan port, cek `UsbManager.hasPermission`, minta izin spesifik jika perlu, buka hanya setelah izin untuk perangkat yang sama diberikan. Saat dicabut: batalkan I/O, tutup port dan connection, buang antrean, kunci tulis. Saat dipasang kembali: cek izin lagi, jangan melanjutkan transaksi lama.
- [usb-serial-for-android](https://github.com/mik3y/usb-serial-for-android) menyediakan port serial user-space, bukan implementasi Modbus. Kandidat untuk spike: versi 3.12.0 yang dicontohkan upstream. Pin versi, cek lisensi/dependensi dan simpan checksum sebelum distribusi. Rekam VID:PID, interface, driver terpilih, kemampuan 8E1 dan arah TX/RX otomatis. Jangan mengasumsikan chipset dari label ZK-U485. Jika driver tidak cocok, tampilkan identitas dan berhenti; jangan coba driver acak.
- [Modbus Serial Line V1.02, §2.5](https://www.modbus.org/file/secure/modbusoverserial.pdf): RTU memakai CRC dan jeda antarframe. Pada 9600 8E1, 3,5 karakter ≈ 4,01 ms; di atas 19200 baud spesifikasi merekomendasikan 1,75 ms. Callback USB bukan batas frame. Susun potongan byte menurut fungsi, panjang respons dan deadline; periksa unit, fungsi, panjang, CRC, dan echo respons tulis. Uji potongan, frame terlambat, dan local echo: salinan TX FC05 bukan bukti slave menulis.

**Keputusan yang diusulkan:** tambah `android/` di repo yang sama: Kotlin + Compose untuk UI; `UsbManager` + driver serial untuk transport. Satu worker I/O per sesi, satu request aktif per bus. Codec RTU Kotlin murni diuji di JVM; UI menerima status, tidak memiliki port. Berbagi vektor uji dan spesifikasi, bukan runtime desktop. Biaya: codec ketiga harus lolos fixture yang sama. Alternatif .NET Android memungkinkan peninjauan ulang reuse `Protocol.cs` jika protokol berkembang, tetapi tidak menghilangkan kebutuhan transport USB Android maupun UI baru. Membungkus PySide6 juga tidak menyelesaikan dua masalah tersebut. Jangan migrasikan Windows untuk membuka jalan bagi Android.

## PRD: kontrak edisi pertama

1. **Hubungkan:** operator memilih port dan parameter serial dari manual perangkat. Bedakan host tidak tersedia, adaptor tidak terdeteksi, driver tidak didukung, izin ditolak, port sibuk dan port terbuka. Membuka port tidak mengirim transaksi atau membuktikan perangkat telah merespons.
2. **Baca:** pilih FC01–04, unit unicast 1–247, alamat protokol berbasis nol, jumlah, interval dan timeout. Tampilkan Read Once, Start/Stop, waktu respons, nilai terakhir beserta umur dan status. Exception Modbus, CRC dan timeout tidak boleh terlihat seperti nilai nol atau data terkini.
3. **Tulis terbatas:** FC05/06, terkunci setiap sesi. Tampilkan adaptor, unit, fungsi, alamat dan nilai sebelum konfirmasi. Tolak broadcast, nilai/jumlah ilegal dan replay. Jika respons hilang, tampilkan *hasil tidak diketahui*, jangan retry otomatis. Tawarkan readback terpisah untuk titik yang aman dibaca. Catat maksud, hasil dan waktu secara lokal. Sakelar ini perlindungan operator, bukan autentikasi.
4. **Siklus hidup:** foreground-only. Rotasi tidak menggandakan worker; pindah ke background menghentikan polling dan menutup sesi. Kembali ke aplikasi memerlukan Connect lagi. Tidak ada restore write setelah proses mati. Tanpa izin internet untuk edisi USB ini.
5. **Di luar rilis pertama:** TCP/UDP, ASCII, raw console, multi-window, scan otomatis, polling background, tren dan impor workspace. Prioritaskan berikutnya dari kebutuhan pilot; dokumentasikan perbedaan di tabel fitur Windows/Python/Android.

## Sprint plan: urutan kerja dan bukti selesai

| Urutan / pemilik peran | Story | Gate penerimaan yang diusulkan |
|---|---|---|
| S0 · hardware + Android | Kualifikasi dua kelas perangkat: satu sensor referensi dan satu inverter/VFD dengan manual model serta firmware yang diketahui. Catat HP/OS, OTG, driver dan konfigurasi bus. | Bandingkan hasil baca dengan modpoll pada laptop, satu master bergantian. Untuk inverter pilih register monitor baca-saja dari manual; jangan mengirim perintah gerak. Rekam fungsi, offset PDU, jumlah, raw dan skala. Ulangi pada OTG 2.0/3.0, termasuk izin ditolak dan cabut/pasang. |
| S1 · protokol | Implementasikan codec dan transaksi satu-per-satu; bangun fixture dari spec dan tes desktop. | JVM lulus vektor FC01–06, exception, CRC salah, unit salah, frame parsial, timeout, respons terlambat dan batas alamat/jumlah; tidak ada transaksi bertumpuk. |
| S2 · Android + QA | Alur hubungkan → baca sekali → polling → stop; pemulihan lifecycle. | Pada bench terkontrol: 1.000 read berurutan tanpa salah nilai/unit; ukur timeout dan latensi p95, selidiki setiap error. Sepuluh siklus cabut/pasang tanpa crash, port bocor, atau worker ganda. Setelah background tidak ada TX baru. |
| S3 · operator + QA | Verifikasi proteksi tulis pada simulator RTU atau register scratch yang disetujui untuk bench dan tidak terhubung ke aktuator. | Terkunci/batal = nol frame write; verifikasi FC05 dengan FC01 dan FC06 dengan FC03. Cabut setelah TX: hasil tidak diketahui, nol replay. Jangan gunakan register kontrol inverter untuk uji komunikasi. |
| S4 · release | APK rilis bertanda tangan, checksum, panduan sambungan/troubleshooting, matriks perangkat dan batas fitur. | Instal APK bersih dan upgrade pada ≥2 model HP dengan versi Android berbeda; cantumkan hanya kombinasi yang benar-benar lulus. Pisahkan artefak Android dari ZIP Windows. Jalankan self-test Windows dan tes Python terkait jika fixture bersama berubah; jangan mengubah paket desktop untuk rilis mobile. |

**Readiness BMAD: CONCERNS.** Siap menjalankan S0, belum siap menjanjikan kompatibilitas hardware atau tanggal rilis. Keputusan setelah S0: pilih minimum Android berdasarkan armada HP nyata dan dukungan dependensi; tetapkan daftar adaptor awal berdasarkan hasil, bukan nama dagang. Jika hanya satu konfigurasi lolos, publikasikan sebagai pilot untuk konfigurasi itu. Jika FC01 stabil tetapi arah TX/RX atau echo write belum tervalidasi, pertahankan pilot read-only. EXE Windows tetap jalur operasional selama pilot.

## Pembanding lapangan: Soil-Sensor-Android 0.7.0

Pembaca BGT-SMPS delapan parameter adalah satu pembanding FC03, bukan definisi produk. Catatan `docs/operations/verification-0.7.0.md` pada proyek referensi (27 Oktober 2026) menyatakan pengguna memperoleh pembacaan normal dari sensor USB; ini bukan kualifikasi APK MultiPoll atau perangkat lain.

| Aspek | Acuan pengembangan MultiPoll |
|---|---|
| Serial | Khusus sensor ini: unit 1, **9600 8N1**, FC03, alamat PDU 0, jumlah 8, interval 1000 ms; lihat [profil BGT-SMPS desktop](../../openpoll-win/profiles/bgt-smps.json). Jangan terapkan parameter ini pada inverter atau sensor lain tanpa manual. |
| Perangkat | Referensi mengenali CH340 `1A86:7523`. Identitas itu milik konverter, bukan sensor. Di MultiPoll pilih port secara eksplisit; jangan mengubah fungsi, izin tulis, atau memulai polling hanya berdasarkan VID:PID. Sensor memakai catu 12 V terpisah dari USB HP. |
| Transaksi | `crates/soil-core/src/protocol.rs` di proyek asal meminta delapan register sekaligus; respons normal 21 byte, termasuk 16 byte data. USB dapat mengirim frame dalam beberapa potongan. Pada CH340, `UsbInput.kt` menggunakan pembacaan sampai hening karena purge hardware tidak selalu tersedia. Bedakan port terbuka dari sampel valid (unit, fungsi, panjang dan CRC cocok). |
| Interpretasi | Simpan delapan register mentah sebagai pembanding. Suhu menggunakan `int16(raw[0]) / 10` (termasuk negatif), kelembapan `raw[1] / 10`, register 2–6 tanpa skala, pH `raw[7] / 100`. Untuk tampilan satuan gunakan profil desktop; jangan menerapkan skala BGT ke sensor generik atau menimpa nilai mentah. |

**Uji penerimaan berikutnya:** baca sekali dengan aplikasi asal dan catat konfigurasi serta delapan nilai mentah. Putuskan master asal; sambungkan MultiPoll pada sensor dan dongle yang sama dengan setelan tabel. Pastikan urutan kanal dan panjang respons benar. Untuk pembandingan angka yang persis, pakai ulang satu frame acuan yang sama; dua pengukuran fisik berurutan dapat berbeda karena kondisi tanah berubah. Uji cabut/pasang dan kegagalan respons tanpa mengirim write. Catat HP, versi OS dan jenis OTG untuk setiap hasil. Tidak perlu menyalin data riwayat, APK, logo, atau driver Windows proyek asal.

**Prioritas produk:** pertahankan polling manual tanpa profil sebagai alur utama. Prioritaskan interpretasi register bertanda/multiword, urutan word, skala dan satuan yang dapat dikonfigurasi; tampilkan raw sebagai pembanding. Profil vendor opsional, bukan syarat koneksi. Jangan klaim kesetaraan semua fungsi modpoll atau kompatibilitas semua inverter.
