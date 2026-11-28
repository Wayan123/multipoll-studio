package id.multipoll.field

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

private val writeFunctions = listOf("FC05 · Satu coil", "FC06 · Satu register")
private val coilStates = listOf("0 · Mati", "1 · Aktif")
private data class WriteOrder(val target: String, val unit: Int, val fc: Int, val address: Int, val value: Int, val timeout: Int) {
    val readFc get() = if (fc == 5) 1 else 3
    val displayValue get() = if (fc == 5) coilStates[value] else value.toString()
}

@Composable
internal fun WriteScreen(session: FieldSession, state: SessionView, initialUnit: String, verify: (ReadSetup) -> Unit) {
    var fc by remember { mutableIntStateOf(5) }
    var unit by remember { mutableStateOf(initialUnit) }
    var address by remember { mutableStateOf("") }
    var rawValue by remember { mutableStateOf("") }
    var timeout by remember { mutableStateOf("1000") }
    var pending by remember(state.unlocked) { mutableStateOf<WriteOrder?>(null) }
    var submitted by remember { mutableStateOf<WriteOrder?>(null) }
    val canEdit = state.idle && state.unlocked

    submitted?.let { last ->
        Surface(color = FieldColors.primaryContainer, contentColor = FieldColors.onPrimaryContainer,
            shape = ControlShape, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Verifikasi titik terakhir", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                Text("Unit ${last.unit} · FC0${last.readFc} · Alamat ${last.address}\nNilai untuk dibandingkan: ${last.displayValue}",
                    fontFamily = FontFamily.Monospace, style = MaterialTheme.typography.bodyMedium)
                Text("Respons tulis saja belum membuktikan nilai berubah. Baca balik hanya jika titik ini aman dibaca.",
                    style = MaterialTheme.typography.bodyMedium)
                SecondaryAction("Baca balik dengan FC0${last.readFc}", state.idle, {
                    if (session.connected && session.target == last.target && !session.busy && !session.polling) {
                        verify(ReadSetup(unit = last.unit.toString(), fc = last.readFc, address = last.address.toString(),
                            count = "1", timeout = last.timeout.toString()))
                    }
                })
            }
        }
    }
    Section("Izin tulis sesi")
    Row(Modifier.fillMaxWidth().toggleable(value = state.unlocked, role = Role.Switch, enabled = state.idle,
        onValueChange = session::unlock).padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(if (state.unlocked) "Tulis diizinkan" else "Tulis terkunci", fontWeight = FontWeight.Bold)
            Hint(when {
                !state.connected -> "Hubungkan adaptor di tab Port."
                state.polling -> "Hentikan polling di tab Baca terlebih dahulu."
                state.unlocked -> "Matikan izin untuk mengunci kembali."
                else -> "Aktifkan hanya untuk titik uji yang diizinkan."
            })
        }
        Spacer(Modifier.width(12.dp))
        Switch(checked = state.unlocked, onCheckedChange = null, enabled = state.idle)
    }
    if (!state.unlocked) return
    Section("Perintah tulis")
    Choice("Fungsi", writeFunctions[fc - 5], writeFunctions, canEdit) {
        if (fc != it + 5) { fc = it + 5; address = ""; rawValue = "" }
    }
    PairFields({ NumberInput("Unit (1–247)", unit, canEdit) { unit = it } },
        { NumberInput("Alamat (basis 0)", address, canEdit) { address = it } })
    if (fc == 5) Choice("Nilai coil", rawValue.toIntOrNull()?.let { coilStates.getOrNull(it) } ?: "Pilih nilai",
        coilStates, canEdit) { rawValue = it.toString() }
    else NumberInput("Nilai register (0–65535)", rawValue, canEdit) { rawValue = it }
    NumberInput("Timeout (ms)", timeout, canEdit) { timeout = it }
    val u = unit.toIntOrNull() ?: -1
    val a = address.toIntOrNull() ?: -1
    val v = rawValue.toIntOrNull() ?: -1
    val t = timeout.toIntOrNull() ?: -1
    val error = when {
        u !in 1..247 -> "Unit harus antara 1 dan 247. Broadcast tidak diizinkan."
        a !in 0..65535 -> "Isi alamat protokol antara 0 dan 65535."
        fc == 5 && v !in 0..1 -> "Pilih nilai coil yang akan ditulis."
        v !in 0..65535 -> "Isi nilai register antara 0 dan 65535."
        t !in 100..10000 -> "Timeout harus antara 100 dan 10000 ms."
        else -> null
    }
    if (error != null) Hint(error)
    Action("Tinjau perintah", canEdit && error == null, { pending = WriteOrder(state.target, u, fc, a, v, t) })

    pending?.let { order ->
        AlertDialog(onDismissRequest = { pending = null }, title = { Text("Kirim perintah tulis?") },
            text = {
                Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(order.target, fontWeight = FontWeight.Bold)
                    Text("Unit: ${order.unit}\nFungsi: ${writeFunctions[order.fc - 5]}\nAlamat (basis 0): ${order.address}\nNilai: ${order.displayValue}",
                        fontFamily = FontFamily.Monospace)
                    Text("Pastikan ini titik yang diizinkan. Perintah dikirim sekali. Jika respons hilang, hasilnya tidak diketahui.")
                }
            },
            dismissButton = { TextButton(onClick = { pending = null }) { Text("Batal") } },
            confirmButton = { Button(enabled = canEdit && order.target == state.target,
                colors = ButtonDefaults.buttonColors(containerColor = FieldColors.error), onClick = {
                    if (pending == order && session.connected && session.unlocked && session.target == order.target &&
                        !session.busy && !session.polling) {
                        pending = null
                        submitted = order
                        session.write(order.unit, order.fc, order.address, order.value, order.timeout)
                        rawValue = ""
                    } else pending = null
                }) { Text("Kirim sekali") } })
    }
}
