package com.softbox.phonelab.agent

import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import kotlin.concurrent.thread

/** Local-only HTTP bridge. Windows connects by ADB port forwarding. */
class LocalAgentServer(private val snapshotProvider: () -> JSONObject) {
    @Volatile private var running = false
    @Volatile private var server: ServerSocket? = null
    private var worker: Thread? = null

    @Synchronized fun start() {
        if (running) return
        running = true
        worker = thread(name = "PhoneLab-Agent-Bridge", isDaemon = true) {
            try {
                server = ServerSocket(27831, 8, InetAddress.getByName("127.0.0.1"))
                while (running) {
                    try {
                        val client = server?.accept() ?: break
                        thread(name = "PhoneLab-Agent-Request", isDaemon = true) { handle(client) }
                    } catch (_: Exception) {
                        if (running) continue else break
                    }
                }
            } catch (_: Exception) {
                running = false
            }
        }
    }

    @Synchronized fun stop() {
        running = false
        try { server?.close() } catch (_: Exception) {}
        server = null
        worker = null
    }

    private fun handle(socket: Socket) {
        socket.use { client ->
            try {
                client.soTimeout = 3000
                val reader = BufferedReader(InputStreamReader(client.getInputStream(), Charsets.UTF_8))
                val request = reader.readLine() ?: return
                while (true) {
                    val header = reader.readLine() ?: break
                    if (header.isEmpty()) break
                }
                val endpoint = (request.split(" ").getOrNull(1) ?: "/").substringBefore("?")
                val body = when (endpoint) {
                    "/health" -> JSONObject().put("status", "ok").put("agentVersion", "0.1.0")
                    "/snapshot" -> snapshotProvider()
                    else -> JSONObject().put("error", "not_found")
                }.toString()
                val status = if (endpoint == "/health" || endpoint == "/snapshot") "200 OK" else "404 Not Found"
                val bytes = body.toByteArray(Charsets.UTF_8)
                val header = "HTTP/1.1 $status\r\nContent-Type: application/json; charset=utf-8\r\n" +
                    "Content-Length: ${bytes.size}\r\nConnection: close\r\n\r\n"
                val output = client.getOutputStream()
                output.write(header.toByteArray(Charsets.US_ASCII))
                output.write(bytes)
                output.flush()
            } catch (_: Exception) {
                // The desktop retries if a connection closes during app lifecycle changes.
            }
        }
    }
}
