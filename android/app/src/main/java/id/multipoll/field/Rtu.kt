package id.multipoll.field

/** One Modbus RTU client request and its matching server response (FC01–06). */
internal object Rtu {
    private fun byte(b: Byte) = b.toInt() and 255
    private fun word(a: ByteArray, i: Int) = (byte(a[i]) shl 8) or byte(a[i + 1])

    private fun crc(a: ByteArray, n: Int): Int {
        var crc = 0xffff
        for (i in 0 until n) {
            crc = crc xor byte(a[i])
            repeat(8) { crc = (crc ushr 1) xor (if (crc and 1 != 0) 0xa001 else 0) }
        }
        return crc
    }

    fun request(unit: Int, fc: Int, address: Int, argument: Int): ByteArray {
        require(unit in 1..247 && address in 0..65535) { "Unit atau alamat di luar rentang" }
        val value = when (fc) {
            in 1..4 -> {
                val max = if (fc <= 2) 2000 else 125
                require(argument in 1..max && address + argument <= 65536) { "Rentang baca tidak valid" }
                argument
            }
            5 -> { require(argument in 0..1) { "Nilai coil harus 0 atau 1" }; argument * 0xff00 }
            6 -> { require(argument in 0..65535) { "Nilai register harus 0–65535" }; argument }
            else -> throw IllegalArgumentException("Fungsi tidak didukung")
        }
        return byteArrayOf(unit.toByte(), fc.toByte(), (address ushr 8).toByte(), address.toByte(),
            (value ushr 8).toByte(), value.toByte(), 0, 0).also {
            val check = crc(it, 6)
            it[6] = check.toByte(); it[7] = (check ushr 8).toByte()
        }
    }

    /** Validate the prefix before trusting it to size reads from an untrusted bus. */
    fun responseLength(request: ByteArray, buf: ByteArray, size: Int): Int {
        if (size < 2) return -1
        require(buf[0] == request[0]) { "Unit respons berbeda" }
        val fc = byte(request[1])
        if (byte(buf[1]) == (fc or 0x80)) return 5
        require(buf[1] == request[1]) { "Fungsi respons berbeda" }
        if (fc >= 5) return 8
        if (size < 3) return -1
        val count = word(request, 4)
        val bytes = if (fc <= 2) (count + 7) / 8 else count * 2
        require(byte(buf[2]) == bytes) { "Jumlah byte respons berbeda" }
        return bytes + 5
    }

    fun decode(request: ByteArray, response: ByteArray): List<Int> {
        require(responseLength(request, response, response.size) == response.size) { "Panjang frame tidak valid" }
        val end = response.size - 2
        require(crc(response, end) == (byte(response[end]) or (byte(response[end + 1]) shl 8))) { "CRC respons salah" }
        require(response[1] == request[1]) { "Exception Modbus 0x%02X".format(byte(response[2])) }
        val fc = byte(request[1]); val count = word(request, 4)
        if (fc >= 5) {
            require(request.contentEquals(response)) { "Alamat atau nilai balasan tulis berbeda" }
            return emptyList()
        }
        return List(count) { i -> if (fc <= 2) (byte(response[3 + i / 8]) ushr (i % 8)) and 1 else word(response, 3 + 2 * i) }
    }
}
