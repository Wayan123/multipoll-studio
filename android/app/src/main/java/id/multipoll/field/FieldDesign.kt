package id.multipoll.field

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

private val Enclosure = Color(0xff18332f)
private val PanelInk = Color(0xfff3f8ef)
private val PanelLegend = Color(0xffbdd4c7)
internal val FieldColors = lightColorScheme(
    primary = Color(0xff006a55), onPrimary = Color.White,
    primaryContainer = Color(0xffceeddd), onPrimaryContainer = Enclosure,
    secondary = Color(0xff46645a), secondaryContainer = Color(0xffdcebe0), onSecondaryContainer = Enclosure,
    background = Color(0xfff8f8f2), onBackground = Color(0xff172521),
    surface = Color(0xfff8f8f2), onSurface = Color(0xff172521),
    surfaceVariant = Color(0xffe6eae1), onSurfaceVariant = Color(0xff48574f),
    outline = Color(0xff747f76), outlineVariant = Color(0xffc3cdc3),
    error = Color(0xffa43520), errorContainer = Color(0xffffe2d7), onErrorContainer = Color(0xff5c1b09)
)
internal val ControlShape = RoundedCornerShape(8.dp)

@Composable
internal fun ConsoleHeading(title: String) {
    Column(Modifier.padding(top = 20.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically) {
            Text("MultiPoll Studio", modifier = Modifier.weight(1f), style = MaterialTheme.typography.titleMedium,
                color = FieldColors.primary, fontWeight = FontWeight.Bold)
            Text("MODBUS RTU", style = MaterialTheme.typography.labelMedium, color = FieldColors.onSurfaceVariant,
                fontFamily = FontFamily.Monospace)
        }
        Spacer(Modifier.height(12.dp))
        Text(title, style = MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold,
            letterSpacing = (-0.8).sp, modifier = Modifier.semantics { heading() })
    }
}

@Composable
internal fun InstrumentGlyph(index: Int) {
    val ink = LocalContentColor.current
    Canvas(Modifier.size(24.dp)) {
        val u = size.minDimension / 24f
        fun point(x: Int, y: Int) = Offset(x * u, y * u)
        fun line(x: Int, y: Int, a: Int, b: Int) = drawLine(ink, point(x,y), point(a,b), 2f*u)
        when (index) {
            0 -> {
                drawRect(ink, point(5,8), Size(14*u, 8*u), style = Stroke(2*u))
                line(9,3,9,8); line(15,3,15,8); line(12,16,12,21)
            }
            1 -> {
                line(2,15,7,15); line(7,15,7,7); line(7,7,14,7)
                line(14,7,14,17); line(14,17,21,17)
            }
            else -> {
                line(4,18,20,18); line(17,4,8,13); line(8,13,7,16)
                line(7,16,10,15); line(10,15,19,6); line(19,6,17,4)
            }
        }
    }
}

@Composable
internal fun StatusPanel(state: SessionView) {
    val mode = when {
        state.polling -> "Polling aktif"
        state.busy -> "Sedang memproses"
        state.connected -> "Port terbuka"
        else -> "Port tertutup"
    }
    Surface(shape = RoundedCornerShape(12.dp), color = Enclosure, contentColor = PanelInk,
        modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Spacer(Modifier.size(9.dp).background(if (state.connected) Color(0xff9ee59b) else PanelLegend,
                    RoundedCornerShape(2.dp)))
                Text(mode, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            }
            if (state.connected && state.target.isNotBlank()) Text(state.target, color = PanelLegend,
                style = MaterialTheme.typography.bodySmall, fontFamily = FontFamily.Monospace)
            if (state.status.isNotBlank()) Text(state.status, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

@Composable
internal fun Section(title: String) {
    Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold,
        modifier = Modifier.semantics { heading() })
}

@Composable
internal fun Hint(text: String) {
    Text(text, style = MaterialTheme.typography.bodyMedium, color = FieldColors.onSurfaceVariant)
}

@Composable
internal fun Choice(label: String, value: String, choices: List<String>, enabled: Boolean,
                    onChoose: (Int) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        OutlinedButton(onClick = { open = true }, enabled = enabled && choices.isNotEmpty(), shape = ControlShape,
            modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp),
            contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp)) {
            Column(Modifier.weight(1f)) {
                Text(label, style = MaterialTheme.typography.labelSmall)
                Text(value, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium)
            }
            Spacer(Modifier.width(8.dp))
            Text("▾")
        }
        DropdownMenu(expanded = open && enabled, onDismissRequest = { open = false }) {
            choices.forEachIndexed { i, choice -> DropdownMenuItem(text = { Text(choice) }, onClick = {
                open = false; onChoose(i)
            }) }
        }
    }
}

@Composable
internal fun NumberInput(label: String, value: String, enabled: Boolean, onChange: (String) -> Unit) {
    OutlinedTextField(value = value, onValueChange = onChange, enabled = enabled,
        modifier = Modifier.fillMaxWidth(), label = { Text(label) }, shape = ControlShape,
        singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        textStyle = MaterialTheme.typography.bodyLarge.copy(fontFamily = FontFamily.Monospace))
}

@Composable
internal fun PairFields(first: @Composable () -> Unit, second: @Composable () -> Unit) {
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        if (maxWidth.value / LocalDensity.current.fontScale < 300f) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { first(); second() }
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Box(Modifier.weight(1f)) { first() }
                Box(Modifier.weight(1f)) { second() }
            }
        }
    }
}

@Composable
internal fun Action(label: String, enabled: Boolean, onClick: () -> Unit, danger: Boolean = false) {
    Button(onClick = onClick, enabled = enabled, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
        colors = ButtonDefaults.buttonColors(containerColor = if (danger) FieldColors.error else FieldColors.primary,
            contentColor = Color.White), shape = ControlShape) { Text(label) }
}

@Composable
internal fun SecondaryAction(label: String, enabled: Boolean, onClick: () -> Unit) {
    OutlinedButton(onClick = onClick, enabled = enabled, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
        shape = ControlShape) { Text(label) }
}
