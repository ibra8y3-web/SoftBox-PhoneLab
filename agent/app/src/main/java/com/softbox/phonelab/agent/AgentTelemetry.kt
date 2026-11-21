package com.softbox.phonelab.agent

import android.content.Context
import android.os.Build
import android.util.Log
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.PrintWriter
import java.io.StringWriter
import java.time.Instant

/**
 * Bounded structured local telemetry. Crash reports remain private to Agent storage and are
 * shared with the Windows app only when the technician explicitly exports diagnostics.
 */
object AgentTelemetry {
    private const val TAG = "SoftBoxPhoneLab"
    private const val EVENTS_FILE = "agent-events.jsonl"
    private const val CRASH_FILE = "agent-crashes.jsonl"
    private const val MAX_FILE_BYTES = 192 * 1024
    private val lock = Any()
    @Volatile private var installed = false

    fun install(context: Context) {
        if (installed) return
        synchronized(lock) {
            if (installed) return
            val appContext = context.applicationContext
            val previous = Thread.getDefaultUncaughtExceptionHandler()
            Thread.setDefaultUncaughtExceptionHandler { thread, error ->
                captureCrash(appContext, thread, error)
                if (previous != null) previous.uncaughtException(thread, error)
                else {
                    android.os.Process.killProcess(android.os.Process.myPid())
                    kotlin.system.exitProcess(10)
                }
            }
            installed = true
        }
        record(context, "agent_started", JSONObject().put("apiLevel", Build.VERSION.SDK_INT))
    }

    fun record(context: Context, event: String, fields: JSONObject = JSONObject()) {
        val entry = JSONObject()
            .put("timestampUtc", Instant.now().toString())
            .put("event", event.take(80))
            .put("agentVersion", "0.6.0")
            .put("fields", fields)
        Log.i(TAG, entry.toString())
        appendBounded(File(context.applicationContext.filesDir, EVENTS_FILE), entry.toString())
    }

    fun summary(context: Context): JSONObject {
        val events = readLastLines(File(context.applicationContext.filesDir, EVENTS_FILE), 20)
        val crashes = readLastLines(File(context.applicationContext.filesDir, CRASH_FILE), 20)
        val latestEvent = events.lastOrNull()?.let { runCatching { JSONObject(it) }.getOrNull() }
        val latestCrash = crashes.lastOrNull()?.let { runCatching { JSONObject(it) }.getOrNull() }
        return JSONObject()
            .put("eventCountSampled", events.size)
            .put("crashCountSampled", crashes.size)
            .put("lastEvent", latestEvent?.optString("event") ?: JSONObject.NULL)
            .put("lastEventAtUtc", latestEvent?.optString("timestampUtc") ?: JSONObject.NULL)
            .put("lastCrashType", latestCrash?.optString("exceptionType") ?: JSONObject.NULL)
            .put("lastCrashAtUtc", latestCrash?.optString("timestampUtc") ?: JSONObject.NULL)
    }

    fun exportReport(context: Context): JSONObject {
        val events = readLastLines(File(context.applicationContext.filesDir, EVENTS_FILE), 300)
        val crashes = readLastLines(File(context.applicationContext.filesDir, CRASH_FILE), 20)
        return JSONObject()
            .put("format", "softbox-agent-diagnostics-v1")
            .put("createdAtUtc", Instant.now().toString())
            .put("device", JSONObject()
                .put("manufacturer", Build.MANUFACTURER)
                .put("model", Build.MODEL)
                .put("androidVersion", Build.VERSION.RELEASE)
                .put("apiLevel", Build.VERSION.SDK_INT))
            .put("events", JSONArray(events.mapNotNull { runCatching { JSONObject(it) }.getOrNull() }))
            .put("crashes", JSONArray(crashes.mapNotNull { runCatching { JSONObject(it) }.getOrNull() }))
            .put("privacy", "Local diagnostic export requested by the technician. No IMEI, IMSI, ICCID, phone number, location coordinates, account credentials or biometric data are collected by this telemetry layer.")
    }

    private fun captureCrash(context: Context, thread: Thread, error: Throwable) {
        val stack = StringWriter()
        error.printStackTrace(PrintWriter(stack))
        val entry = JSONObject()
            .put("timestampUtc", Instant.now().toString())
            .put("thread", thread.name.take(120))
            .put("exceptionType", error.javaClass.name.take(240))
            .put("message", (error.message ?: "").take(2000))
            .put("stackTrace", stack.toString().take(48_000))
            .put("agentVersion", "0.6.0")
            .put("androidApi", Build.VERSION.SDK_INT)
        Log.e(TAG, "Uncaught Agent exception", error)
        appendBounded(File(context.applicationContext.filesDir, CRASH_FILE), entry.toString())
    }

    private fun appendBounded(file: File, line: String) {
        synchronized(lock) {
            runCatching {
                if (file.exists() && file.length() > MAX_FILE_BYTES) {
                    val retained = readLastLines(file, 300)
                    file.writeText(retained.joinToString(separator = "\n", postfix = if (retained.isEmpty()) "" else "\n"))
                }
                file.appendText(line + "\n")
            }.onFailure { Log.w(TAG, "Could not persist local telemetry", it) }
        }
    }

    private fun readLastLines(file: File, maxLines: Int): List<String> {
        if (!file.exists()) return emptyList()
        return runCatching { file.useLines { sequence -> sequence.filter { it.isNotBlank() }.toList().takeLast(maxLines) } }
            .getOrDefault(emptyList())
    }
}
