package id.multipoll.field

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp

class MainActivity : ComponentActivity() {
    private lateinit var session: FieldSession
    private var revision by mutableIntStateOf(0)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val bars = SystemBarStyle.light(android.graphics.Color.TRANSPARENT, android.graphics.Color.TRANSPARENT)
        enableEdgeToEdge(statusBarStyle = bars, navigationBarStyle = bars)
        session = FieldSession(this) { runOnUiThread { revision++ } }
        setContent {
            val state = remember(revision) { SessionView(session) }
            MaterialTheme(colorScheme = FieldColors) { FieldConsole(session, state) }
        }
    }

    override fun onStart() {
        super.onStart()
        session.refresh()
    }

    override fun onStop() {
        session.disconnect()
        super.onStop()
    }

    override fun onDestroy() {
        session.destroy()
        super.onDestroy()
    }
}

internal class SessionView(session: FieldSession) {
    val devices = session.devices.toList()
    val connected = session.connected
    val busy = session.busy
    val polling = session.polling
    val unlocked = session.unlocked
    val status = session.status
    val target = session.target
    val sample = session.sample
    val sampleTimeMs = session.sampleTimeMs
    val idle get() = connected && !busy && !polling
}

internal data class ReadSetup(
    val unit: String = "1", val fc: Int = 3, val address: String = "0", val count: String = "1",
    val timeout: String = "1000", val interval: String = "1000"
)

internal data class PortSetup(val name: String? = null, val baud: Int = 19200, val parity: Int = 0, val stop: Int = 2)

private enum class Pane(val label: String, val title: String) {
    PORT("Port", "Sambungan USB"), READ("Baca", "Pembacaan"), WRITE("Tulis", "Penulisan")
}

@Composable
private fun FieldConsole(session: FieldSession, state: SessionView) {
    var pane by remember { mutableStateOf(Pane.PORT) }
    var port by remember { mutableStateOf(PortSetup()) }
    var read by remember { mutableStateOf(ReadSetup()) }
    val imeOpen = WindowInsets.ime.getBottom(LocalDensity.current) > 0
    Surface(color = FieldColors.background, modifier = Modifier.fillMaxSize()) {
        Column(Modifier.safeDrawingPadding().imePadding()) {
            key(pane) {
                Column(
                    Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState())
                        .padding(horizontal = 20.dp), horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Column(Modifier.widthIn(max = 640.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                        ConsoleHeading(pane.title)
                        StatusPanel(state)
                        when (pane) {
                            Pane.PORT -> PortScreen(session, state, port, { port = it }, onRead = { pane = Pane.READ })
                            Pane.READ -> ReadScreen(session, state, read) { read = it }
                            Pane.WRITE -> key(state.connected, state.target) {
                                WriteScreen(session, state, read.unit) { verify ->
                                    read = verify
                                    pane = Pane.READ
                                    session.unlock(false)
                                    session.read(verify.unit.toInt(), verify.fc, verify.address.toInt(), 1,
                                        verify.timeout.toInt(), verify.interval.toInt(), false)
                                }
                            }
                        }
                        Spacer(Modifier.height(8.dp))
                    }
                }
            }
            if (!imeOpen) NavigationBar(containerColor = FieldColors.surface, tonalElevation = 0.dp,
                windowInsets = WindowInsets(0, 0, 0, 0)) {
                Pane.entries.forEachIndexed { index, item ->
                    NavigationBarItem(selected = pane == item, onClick = {
                        if (pane == Pane.WRITE && item != Pane.WRITE) session.unlock(false)
                        pane = item
                    }, icon = { InstrumentGlyph(index) }, label = { Text(item.label) })
                }
            }
        }
    }
}
