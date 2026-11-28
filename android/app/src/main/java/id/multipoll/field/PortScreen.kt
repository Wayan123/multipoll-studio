package id.multipoll.field

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

private val baudRates = listOf(1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200)
private val baudLabels = baudRates.map(Int::toString)
private val parities = listOf("Tanpa (N)", "Ganjil (O)", "Genap (E)")
private val stopBits = listOf("1", "2")

@Composable
internal fun PortScreen(session: FieldSession, state: SessionView, port: PortSetup,
                        setPort: (PortSetup) -> Unit, onRead: () -> Unit) {
    val canEdit = !state.connected && !state.busy
    val selected = port.name?.takeIf { it in state.devices }
    if (state.connected) {
        Section("Parameter port")
        Text("${port.baud} baud  ·  8${"NOE"[port.parity]}${port.stop}",
            fontFamily = FontFamily.Monospace, style = MaterialTheme.typography.titleMedium)
        Hint("Membuka port tidak mengirim perintah Modbus. Pilih Baca untuk meminta data dari perangkat.")
        Action("Buka pembacaan", true, onRead)
        SecondaryAction("Putuskan sambungan", true, session::disconnect)
        return
    }
    Section("Adaptor dan port")
    if (state.devices.isEmpty()) {
        Surface(color = FieldColors.surfaceVariant, shape = RoundedCornerShape(8.dp)) {
            Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("HP  ›  OTG  ›  USB–RS485", fontFamily = FontFamily.Monospace,
                    style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
                Hint("Pasang dongle melalui adaptor OTG data. Nyalakan perangkat Modbus dengan catu dayanya sendiri, lalu periksa USB lagi.")
            }
        }
    } else Choice("Port USB", selected ?: "Pilih adaptor dan port", state.devices, canEdit) {
        setPort(port.copy(name = state.devices[it]))
    }
    SecondaryAction("Periksa USB lagi", canEdit, session::refresh)
    Section("Parameter serial")
    Choice("Laju baud", port.baud.toString(), baudLabels, canEdit) {
        setPort(port.copy(baud = baudRates[it]))
    }
    PairFields({ Choice("Paritas", parities[port.parity], parities, canEdit) {
        setPort(port.copy(parity = it))
    } }, { Choice("Bit stop", port.stop.toString(), stopBits, canEdit) {
        setPort(port.copy(stop = it + 1))
    } })
    Hint("8 bit data. Gunakan baud, paritas dan bit stop dari manual sensor, inverter atau perangkat RTU. Hanya satu master per bus.")
    Action("Hubungkan port", canEdit && selected != null, {
        val index = session.devices.indexOf(selected)
        if (index >= 0) session.connect(index, port.baud, port.parity, port.stop)
    })
}
