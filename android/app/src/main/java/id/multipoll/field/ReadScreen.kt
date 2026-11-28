package id.multipoll.field

import android.os.SystemClock
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay

private val readFunctions = listOf("FC01 · Coil", "FC02 · Input diskret", "FC03 · Holding register", "FC04 · Input register")

@Composable
internal fun ReadScreen(session: FieldSession, state: SessionView, read: ReadSetup, update: (ReadSetup) -> Unit) {
    SamplePanel(state)
    if (state.polling) Action("Hentikan polling", true, session::stop)
    Section("Permintaan baca")
    val editable = !state.busy && !state.polling
    Choice("Fungsi", readFunctions[read.fc - 1], readFunctions, editable) { update(read.copy(fc = it + 1)) }
    PairFields({ NumberInput("Unit (1–247)", read.unit, editable) { update(read.copy(unit = it)) } },
        { NumberInput("Alamat awal", read.address, editable) { update(read.copy(address = it)) } })
    NumberInput("Jumlah ${if (read.fc <= 2) "bit" else "register"}", read.count, editable) { update(read.copy(count = it)) }
    Hint("Alamat protokol dimulai dari 0. Gunakan offset dari manual, bukan nomor referensi seperti 40001.")
    PairFields({ NumberInput("Timeout (ms)", read.timeout, editable) { update(read.copy(timeout = it)) } },
        { NumberInput("Interval (ms)", read.interval, editable) { update(read.copy(interval = it)) } })
    val unit = read.unit.toIntOrNull() ?: -1
    val address = read.address.toIntOrNull() ?: -1
    val count = read.count.toIntOrNull() ?: -1
    val timeout = read.timeout.toIntOrNull() ?: -1
    val interval = read.interval.toIntOrNull() ?: -1
    val max = if (read.fc <= 2) 2000 else 125
    val error = when {
        unit !in 1..247 -> "Unit harus antara 1 dan 247."
        address !in 0..65535 -> "Alamat awal harus antara 0 dan 65535."
        count !in 1..max -> "Jumlah untuk ${readFunctions[read.fc - 1]} harus antara 1 dan $max."
        count > 65536 - address -> "Alamat akhir melampaui 65535. Kurangi jumlah atau alamat awal."
        timeout !in 100..10000 -> "Timeout harus antara 100 dan 10000 ms."
        else -> null
    }
    val validInterval = interval in 100..60000
    if (error != null || !validInterval) Text(error ?: "Interval polling harus antara 100 dan 60000 ms.",
        color = FieldColors.error, style = MaterialTheme.typography.bodyMedium)
    if (!state.connected) Hint("Hubungkan adaptor di tab Port sebelum membaca.")
    Action("Baca sekali", state.idle && error == null, {
        session.read(unit, read.fc, address, count, timeout, if (validInterval) interval else 1000, false)
    })
    if (state.polling) SecondaryAction("Hentikan polling", true, session::stop)
    else SecondaryAction("Mulai polling", state.idle && error == null && validInterval, {
        session.read(unit, read.fc, address, count, timeout, interval, true)
    })
}

@Composable
private fun SamplePanel(state: SessionView) {
    var expanded by remember { mutableStateOf(false) }
    Surface(modifier = Modifier.fillMaxWidth(), color = FieldColors.surfaceVariant, shape = RoundedCornerShape(8.dp)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text("Sampel terakhir", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            if (state.sample.isBlank() || state.sampleTimeMs == 0L) {
                Hint(when {
                    !state.connected -> "Belum ada sampel. Buka port, lalu pilih Baca sekali."
                    state.busy || state.polling -> "Menunggu hasil pembacaan pertama."
                    else -> "Belum ada sampel. Pilih fungsi dan alamat, lalu baca sekali."
                })
            } else {
                SelectionContainer {
                    Text(state.sample, fontFamily = FontFamily.Monospace, style = MaterialTheme.typography.bodyLarge,
                        maxLines = if (expanded) Int.MAX_VALUE else 4, overflow = TextOverflow.Ellipsis)
                }
                if (state.sample.length > 48 || state.sample.count { it == '\n' } >= 3) {
                    TextButton(onClick = { expanded = !expanded }) { Text(if (expanded) "Ringkas" else "Lihat semua nilai") }
                }
                SampleAge(state.sampleTimeMs)
            }
        }
    }
}

@Composable
private fun SampleAge(receivedAt: Long) {
    val seconds by produceState(0L, receivedAt) {
        while (true) {
            value = ((SystemClock.elapsedRealtime() - receivedAt) / 1000).coerceAtLeast(0)
            delay(1000)
        }
    }
    Hint(if (seconds < 60) "Diterima $seconds detik lalu" else "Diterima ${seconds / 60} menit lalu")
}
