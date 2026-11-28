package id.multipoll.field

import org.junit.Assert.*
import org.junit.Test

class RtuTest {
    private fun hex(s: String) = s.split(' ').map { it.toInt(16).toByte() }.toByteArray()

    @Test fun readsRegistersInNetworkOrder() {
        assertArrayEquals(hex("01 03 00 00 00 0A C5 CD"), Rtu.request(1, 3, 0, 10))
        // Modbus Application Protocol FC03 example, RTU envelope.
        assertEquals(listOf(555, 0, 100), Rtu.decode(Rtu.request(17, 3, 107, 3), hex("11 03 06 02 2B 00 00 00 64 C8 BA")))
    }

    @Test fun coilBitsAreLowBitFirstAndPaddingIsIgnored() {
        val req = Rtu.request(17, 1, 19, 19)
        assertEquals(listOf(1,0,1,1,0,0,1,1, 1,1,0,1,1,0,1,1, 1,0,1),
            Rtu.decode(req, hex("11 01 03 CD DB 05 35 D2")))
    }

    @Test fun onlyExactWriteEchoAccepted() {
        val req = Rtu.request(1, 5, 0, 1)
        assertArrayEquals(hex("01 05 00 00 FF 00 8C 3A"), req)
        Rtu.decode(req, req)
        assertThrows(IllegalArgumentException::class.java) { Rtu.decode(req, Rtu.request(1, 5, 1, 1)) }
    }

    @Test fun badFramesDoNotProduceData() {
        val req = Rtu.request(1, 3, 0, 1)
        for (s in listOf("01", "02 03", "01 04", "01 03 04", "01 03 02 00 01 00 00")) {
            assertThrows(IllegalArgumentException::class.java) { Rtu.decode(req, hex(s)) }
        }
        val failure = assertThrows(IllegalArgumentException::class.java) { Rtu.decode(req, hex("01 83 02 C0 F1")) }
        assertEquals("Exception Modbus 0x02", failure.message)
    }

    @Test fun requestLimitsExcludeBroadcastAndOverflow() {
        for (args in listOf(listOf(0,5,0,1), listOf(1,5,0,2), listOf(1,6,0,65536), listOf(1,4,65535,2), listOf(1,2,0,2001))) {
            assertThrows(IllegalArgumentException::class.java) { Rtu.request(args[0],args[1],args[2],args[3]) }
        }
    }
}
