package com.softbox.phonelab.agent

import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import kotlin.concurrent.thread

/** Local-only HTTP bridge. The server binds to loopback; Windows reaches it only through ADB forward. */
class LocalAgentServer(
    private val snapshotProvider: () -> JSONObject,
    private val commandHandler: (String) -> Boolean
) {
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
                    } catch (_: Exception) { if (running) continue else break }
                }
            } catch (_: Exception) { running = false }
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
                val parts = request.split(" ")
                val method = parts.getOrNull(0) ?: "GET"
                val endpoint = (parts.getOrNull(1) ?: "/").substringBefore("?")
                var contentLength = 0
                while (true) {
                    val headerLine = reader.readLine() ?: break
                    if (headerLine.isEmpty()) break
                    if (headerLine.startsWith("Content-Length:", ignoreCase = true))
                        contentLength = headerLine.substringAfter(":").trim().toIntOrNull()?.coerceIn(0, 4096) ?: 0
                }
                val body = if (contentLength > 0) {
                    val chars = CharArray(contentLength)
                    var offset = 0
                    while (offset < chars.size) {
                        val count = reader.read(chars, offset, chars.size - offset)
                        if (count <= 0) break
                        offset += count
                    }
                    String(chars, 0, offset)
                } else ""

                val statusCode: Int
                val statusText: String
                val response: JSONObject
                when {
                    method == "GET" && endpoint == "/health" -> {
                        statusCode = 200; statusText = "OK"
                        response = JSONObject().put("status", "ok").put("agentVersion", "0.5.0")
                    }
                    method == "GET" && endpoint == "/snapshot" -> {
                        statusCode = 200; statusText = "OK"
                        response = snapshotProvider()
                    }
                    method == "POST" && endpoint == "/command" -> {
                        val command = runCatching { JSONObject(body).optString("command") }.getOrDefault("")
                        val accepted = body.isNotBlank() && runCatching { commandHandler(command) }.getOrDefault(false)
                        statusCode = if (accepted) 202 else 400
                        statusText = if (accepted) "Accepted" else "Bad Request"
                        response = JSONObject().put("accepted", accepted).put("command", command)
                    }
                    method != "GET" && method != "POST" -> {
                        statusCode = 405; statusText = "Method Not Allowed"
                        response = JSONObject().put("error", "method_not_allowed")
                    }
                    else -> {
                        statusCode = 404; statusText = "Not Found"
                        response = JSONObject().put("error", "not_found")
                    }
                }
                val bytes = response.toString().toByteArray(Charsets.UTF_8)
                val header = "HTTP/1.1 " + statusCode + " " + statusText + "\r\n" +
                    "Content-Type: application/json; charset=utf-8\r\n" +
                    "Content-Length: " + bytes.size + "\r\nConnection: close\r\n\r\n"
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