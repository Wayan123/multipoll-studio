package id.multipoll.field

import android.app.Activity
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import com.hoho.android.usbserial.driver.UsbSerialPort
import com.hoho.android.usbserial.driver.UsbSerialProber
import org.json.JSONObject
import java.io.IOException
import java.time.Instant
import java.util.Locale
import java.util.UUID
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit

/** Public operations and UI state are main-thread only. Only the serial worker touches a port. */
class FieldSession(val activity: Activity, val changed: () -> Unit) {
    companion object {
        // Closing a rotated Activity must finish before its replacement opens the same adapter.
        private val io = ThreadPoolExecutor(1, 1, 15, TimeUnit.SECONDS, LinkedBlockingQueue<Runnable>())
            .apply { allowCoreThreadTimeOut(true) }
        private var quarantineUntil = 0L // worker only; survives Activity recreation
    }

    var devices: List<String> = emptyList(); private set
    var connected = false; private set
    var status = "Belum terhubung"; private set
    var sample = ""; private set
    var sampleTimeMs = 0L; private set
    var busy = false; private set
    var polling = false; private set
    var unlocked = false; private set
    var target = ""; private set

    private data class Choice(val device: UsbDevice, val port: UsbSerialPort?, val label: String)
    private data class Setup(val choice: Choice, val baud: Int, val parity: Int, val stop: Int)
    private data class Point(val unit: Int, val fc: Int, val address: Int, val value: Int, val id: String)

    private val usb = activity.getSystemService(UsbManager::class.java)
    private val prober = UsbSerialProber.getDefaultProber()
    private val main = Handler(Looper.getMainLooper())
    private val permissionAction = "${activity.packageName}.USB_PERMISSION.${UUID.randomUUID()}"
    private var permissionIntent: PendingIntent? = null
    private var choices = emptyList<Choice>()
    private var selected: Setup? = null
    private var writing = false
    private var expected: Point? = null
    private var destroyed = false
    @Volatile private var generation = 0L
    private var nextRead: Runnable? = null

    private var port: UsbSerialPort? = null
    private var silenceNs = 0L
    private val chunk = ByteArray(4096)
    private val frame = ByteArray(256)

    private val receiver = object : BroadcastReceiver() {
        @Suppress("DEPRECATION")
        override fun onReceive(context: Context, intent: Intent) {
            if (destroyed) return
            when (intent.action) {
                permissionAction -> {
                    val setup = selected ?: return
                    if (permissionIntent == null || intent.getLongExtra("generation", -1) != generation) return
                    permissionIntent = null
                    // Immutable PendingIntent carries our device selection, not mutable USB extras.
                    if (present(setup.choice.device) && usb?.hasPermission(setup.choice.device) == true) {
                        open(setup, generation)
                    } else end("Izin USB ditolak atau adaptor dilepas")
                }
                UsbManager.ACTION_USB_DEVICE_DETACHED -> {
                    val removed = intent.getParcelableExtra<UsbDevice>(UsbManager.EXTRA_DEVICE)
                    val active = selected?.choice?.device
                    if (active != null && (removed == active || !present(active))) {
                        end("Adaptor USB dilepas")
                        enumerate(updateStatus = false)
                    } else enumerate(updateStatus = selected == null)
                }
                UsbManager.ACTION_USB_DEVICE_ATTACHED -> enumerate(updateStatus = selected == null)
            }
        }
    }

    init {
        val filter = IntentFilter(permissionAction).apply {
            addAction(UsbManager.ACTION_USB_DEVICE_ATTACHED)
            addAction(UsbManager.ACTION_USB_DEVICE_DETACHED)
        }
        if (Build.VERSION.SDK_INT >= 33) activity.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
        else activity.registerReceiver(receiver, filter)
    }

    private fun present(device: UsbDevice): Boolean {
        val current = usb?.deviceList?.get(device.deviceName) ?: return false
        return current.deviceId == device.deviceId && current.vendorId == device.vendorId && current.productId == device.productId
    }

    fun refresh() = enumerate(updateStatus = selected == null)

    private fun enumerate(updateStatus: Boolean) {
        if (destroyed) return
        val host = activity.packageManager.hasSystemFeature(PackageManager.FEATURE_USB_HOST) && usb != null
        try {
            choices = if (!host) emptyList() else usb!!.deviceList.values.sortedBy { it.deviceName }.flatMap { device ->
                val id = String.format(Locale.ROOT, "%04X:%04X · %s", device.vendorId, device.productId, device.deviceName)
                val driver = prober.probeDevice(device)
                if (driver == null || driver.ports.isEmpty()) listOf(Choice(device, null, "$id · driver tidak didukung"))
                else driver.ports.map { Choice(device, it, "$id · ${driver.javaClass.simpleName} · port ${it.portNumber}") }
            }
            devices = choices.map { it.label }
            if (updateStatus) status = when {
                !host -> "USB host tidak tersedia"
                devices.isEmpty() -> "Adaptor USB tidak terdeteksi"
                choices.none { it.port != null } -> "Driver adaptor tidak didukung"
                else -> "Pilih adaptor dan port"
            }
        } catch (e: Exception) {
            choices = emptyList(); devices = emptyList()
            if (updateStatus) status = "Pemindaian USB gagal: ${e.message}"
        }
        changed()
    }

    fun connect(index: Int, baud: Int, parity: Int, stopBits: Int) {
        if (destroyed || connected || busy) return
        val choice = choices.getOrNull(index) ?: return tell("Pilih adaptor USB terlebih dahulu")
        if (choice.port == null) return tell("Driver adaptor tidak didukung: ${choice.label}")
        if (baud !in 300..4_000_000 || parity !in 0..2 || stopBits !in 1..2) return tell("Parameter serial tidak valid")
        val setup = Setup(choice, baud, parity, stopBits)
        selected = setup
        target = "${choice.label} · $baud/8${"NOE"[parity]}$stopBits"
        busy = true; unlocked = false; sample = ""; sampleTimeMs = 0
        val token = ++generation
        try {
            if (!present(choice.device) || usb == null) return end("Adaptor USB tidak lagi terpasang")
            if (usb.hasPermission(choice.device)) open(setup, token) else {
                val intent = Intent(permissionAction).setPackage(activity.packageName).putExtra("generation", token)
                val pending = PendingIntent.getBroadcast(activity, token.toInt(), intent,
                    PendingIntent.FLAG_ONE_SHOT or PendingIntent.FLAG_IMMUTABLE)
                permissionIntent = pending
                usb.requestPermission(choice.device, pending)
                tell("Menunggu izin USB")
            }
        } catch (e: Exception) { end("Gagal meminta akses USB: ${e.message}") }
    }

    private fun open(setup: Setup, token: Long) {
        tell("Membuka port USB")
        io.execute {
            if (generation != token) return@execute
            try {
                val remaining = quarantineUntil - SystemClock.elapsedRealtime()
                if (remaining > 0) throw IOException("Tunggu $remaining ms agar respons lama lewat, lalu hubungkan kembali")
                val device = setup.choice.device
                if (!present(device) || usb?.hasPermission(device) != true) throw IOException("Izin USB hilang atau adaptor dilepas")
                val p = requireNotNull(setup.choice.port)
                val connection = usb.openDevice(device) ?: throw IOException("Port sibuk atau akses USB ditolak")
                try { p.open(connection) } catch (e: Exception) { connection.close(); throw e }
                port = p
                p.setParameters(setup.baud, UsbSerialPort.DATABITS_8, setup.stop, setup.parity)
                val bits = 9 + setup.stop + (if (setup.parity == 0) 0 else 1)
                silenceNs = if (setup.baud > 19200) 1_750_000L else (3_500_000_000L * bits + setup.baud - 1) / setup.baud
                publish(token) { busy = false; connected = true; tell("Siap · tulis terkunci") }
            } catch (e: Exception) {
                closePort()
                publish(token) { end("Port tidak dapat dibuka (akses, port sibuk, atau konfigurasi): ${e.message}") }
            }
        }
    }

    private fun tell(text: String) { status = text; changed() }
    private fun publish(token: Long, action: () -> Unit) {
        main.post { if (!destroyed && generation == token) action() }
    }
    private fun closePort() {
        try { port?.close() } catch (_: IOException) { /* Detached ports may already be closed. */ }
        finally { port = null }
    }

    private fun end(reason: String, keepSample: Boolean = false) {
        ++generation
        permissionIntent?.cancel(); permissionIntent = null
        nextRead?.let(main::removeCallbacks); nextRead = null
        val message = if (writing) "$reason · hasil tulis tidak diketahui; jangan ulang otomatis" else reason
        selected = null; expected = null; writing = false; polling = false; unlocked = false
        connected = false; busy = false; target = ""
        if (!keepSample) { sample = ""; sampleTimeMs = 0 }
        io.execute { closePort() }
        tell(message)
    }

    fun disconnect() { if (!destroyed && selected != null) end("Sesi diputus") }
    fun destroy() {
        if (destroyed) return
        disconnect(); destroyed = true
        activity.unregisterReceiver(receiver)
    }
    fun unlock(enabled: Boolean) {
        if (destroyed || (enabled && (!connected || busy || polling))) return
        if (unlocked != enabled) { unlocked = enabled; changed() }
    }

    fun stop() {
        if (!polling) return
        polling = false
        nextRead?.let(main::removeCallbacks); nextRead = null
        tell(if (busy) "Polling dihentikan setelah pembacaan aktif" else "Polling dihentikan")
    }

    private fun validate(fc: Int, allowed: IntRange, unit: Int, address: Int, arg: Int, timeout: Int): ByteArray? = try {
        require(fc in allowed) { "Fungsi tidak diizinkan" }
        require(timeout in 100..10000) { "Timeout harus 100–10000 ms" }
        Rtu.request(unit, fc, address, arg)
    } catch (e: IllegalArgumentException) { tell(e.message ?: "Parameter tidak valid"); null }

    fun read(unit: Int, fc: Int, address: Int, count: Int, timeout: Int, interval: Int, repeat: Boolean) {
        if (!connected || busy || polling) return
        if (interval !in 100..60000) return tell("Interval harus 100–60000 ms")
        val request = validate(fc, 1..4, unit, address, count, timeout) ?: return
        polling = repeat
        val token = generation
        val verify = expected?.takeIf { !repeat && count == 1 && it.unit == unit && it.address == address && fc == (if (it.fc == 5) 1 else 3) }
        val adapter = target
        fun once() {
            busy = true; tell("Menunggu respons baca")
            io.execute {
                if (token != generation) return@execute
                try {
                    val start = SystemClock.elapsedRealtime()
                    val values = exchange(request, timeout, token)
                    val received = SystemClock.elapsedRealtime()
                    val verdict = if (verify == null) "Pembacaan diterima" else {
                        val result = if (values.single() == verify.value) "Baca balik cocok" else "Baca balik tidak cocok"
                        auditResult(adapter, verify, "$result · terbaca ${values.single()}")
                    }
                    val text = "Unit $unit · FC%02d · alamat %d · %d ms\n%s".format(Locale.ROOT, fc, address, received - start, values.joinToString(", "))
                    publish(token) {
                        sample = text; sampleTimeMs = received; busy = false
                        if (verify != null) expected = null
                        tell(verdict)
                        if (polling) {
                            val task = Runnable { if (token == generation && polling) once() }
                            nextRead = task; main.postDelayed(task, interval.toLong())
                        }
                    }
                } catch (e: Exception) {
                    publish(token) { end("Baca gagal: ${e.message}. Sesi ditutup; data sebelumnya tidak diperbarui", keepSample = true) }
                }
            }
        }
        once()
    }

    fun write(unit: Int, fc: Int, address: Int, value: Int, timeout: Int) {
        if (!connected || busy || polling || !unlocked) return
        val request = validate(fc, 5..6, unit, address, value, timeout) ?: return
        val point = Point(unit, fc, address, value, UUID.randomUUID().toString())
        val token = generation; val adapter = target
        unlocked = false; expected = null; writing = true; busy = true
        tell("Mencatat maksud tulis")
        io.execute {
            try {
                audit(adapter, point, "MAKSUD; tanpa hasil tercatat berarti belum diketahui")
            } catch (e: Exception) {
                publish(token) { writing = false; busy = false; tell("Tidak dikirim: audit gagal (${e.message})") }
                return@execute
            }
            var txStarted = false
            try {
                exchange(request, timeout, token) { txStarted = true }
                val result = auditResult(adapter, point, "Echo diterima; belum terverifikasi. Baca balik titik ini secara terpisah.")
                publish(token) { writing = false; busy = false; expected = point; tell(result) }
            } catch (e: Exception) {
                val result = auditResult(adapter, point, if (txStarted) "Hasil tulis tidak diketahui (${e.message}); tidak diulang" else "Tidak dikirim: ${e.message}")
                publish(token) { writing = false; end("$result. Sesi ditutup", keepSample = true) }
            }
        }
    }

    private fun audit(adapter: String, point: Point, result: String) {
        val record = JSONObject().put("waktu", Instant.now().toString()).put("id", point.id).put("adaptor", adapter)
            .put("unit", point.unit).put("fc", point.fc).put("alamat", point.address).put("nilai", point.value).put("status", result)
        activity.openFileOutput("audit-tulis.jsonl", Context.MODE_APPEND).use { out ->
            out.write((record.toString() + "\n").toByteArray(Charsets.UTF_8)); out.fd.sync()
        }
    }

    private fun auditResult(adapter: String, point: Point, text: String): String = try {
        audit(adapter, point, text); text
    } catch (e: Exception) { "$text · gagal mencatat hasil: ${e.message}" }

    private fun checkActive(token: Long) {
        if (generation != token) throw IOException("Sesi berakhir")
    }

    private fun remainingMs(endNs: Long): Int {
        val left = endNs - SystemClock.elapsedRealtimeNanos()
        if (left <= 0) throw IOException("Timeout RTU")
        return ((left + 999_999L) / 1_000_000L).toInt()
    }

    private fun exchange(request: ByteArray, timeout: Int, token: Long, beforeTx: (() -> Unit)? = null): List<Int> {
        checkActive(token)
        val p = port ?: throw IOException("Port sudah ditutup")
        val end = SystemClock.elapsedRealtimeNanos() + timeout * 1_000_000L
        try {
            // Drain any queued bytes, including a delayed slave ACK following a local write echo.
            var quietSince = SystemClock.elapsedRealtimeNanos()
            do {
                checkActive(token)
                val gap = silenceNs - (SystemClock.elapsedRealtimeNanos() - quietSince)
                if (gap <= 0) break
                val ms = minOf(remainingMs(end), ((gap + 999_999L) / 1_000_000L).toInt())
                if (p.read(chunk, ms) > 0) quietSince = SystemClock.elapsedRealtimeNanos()
            } while (true)
            val txTimeout = remainingMs(end)
            checkActive(token)
            beforeTx?.invoke()
            p.write(request, txTimeout)
            var size = 0
            while (true) {
                checkActive(token)
                val n = p.read(chunk, minOf(remainingMs(end), 100))
                remainingMs(end)
                if (n == 0) continue
                if (n < 0 || n > frame.size - size) throw IOException("Ukuran frame RTU tidak valid")
                chunk.copyInto(frame, size, 0, n); size += n
                val need = Rtu.responseLength(request, frame, size)
                if (need < 0 || size < need) continue
                if (size != need) throw IOException("Byte berlebih pada respons RTU")
                val data = frame.copyOf(size)
                if (request[1].toInt() < 5 && request.contentEquals(data)) throw IOException("Echo lokal bukan respons baca")
                return Rtu.decode(request, data)
            }
        } catch (e: Exception) {
            // RTU has no transaction ID. Never reuse an ambiguous session or immediately re-open it.
            quarantineUntil = SystemClock.elapsedRealtime() + timeout
            closePort()
            throw e
        }
    }
}
