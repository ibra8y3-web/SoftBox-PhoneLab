package com.softbox.phonelab.agent

import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import android.os.Build
import androidx.work.Worker
import androidx.work.WorkerParameters
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.util.concurrent.TimeUnit

/**
 * Periodic WorkManager battery sampler. Android schedules it independently of the Agent Activity
 * and persists the work across ordinary process termination and device reboot.
 */
class BatterySnapshotWorker(
    context: Context,
    parameters: WorkerParameters
) : Worker(context, parameters) {

    override fun doWork(): Result {
        return try {
            val filter = IntentFilter(Intent.ACTION_BATTERY_CHANGED)
            val intent = applicationContext.registerReceiver(null, filter)
                ?: return Result.retry()
            val level = intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
            val scale = intent.getIntExtra(BatteryManager.EXTRA_SCALE, -1)
            if (level < 0 || scale <= 0) return Result.retry()

            val manager = applicationContext.getSystemService(Context.BATTERY_SERVICE) as BatteryManager
            val temperatureRaw = intent.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, -1)
            val chargeCounter = manager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CHARGE_COUNTER)
            val currentNow = manager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CURRENT_NOW)
            val voltage = intent.getIntExtra(BatteryManager.EXTRA_VOLTAGE, -1)
            val status = intent.getIntExtra(BatteryManager.EXTRA_STATUS, BatteryManager.BATTERY_STATUS_UNKNOWN)
            val plugged = intent.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0)
            val sample = JSONObject()
                .put("timestampUtc", Instant.now().toString())
                .put("batteryPercent", level * 100 / scale)
                .put("voltageMillivolts", if (voltage >= 0) voltage else JSONObject.NULL)
                .put("temperatureCelsius", if (temperatureRaw >= 0) temperatureRaw / 10.0 else JSONObject.NULL)
                .put("chargeCounterMicroAh", if (chargeCounter >= 0) chargeCounter else JSONObject.NULL)
                .put("currentNowMicroA", if (currentNow != Int.MIN_VALUE) currentNow else JSONObject.NULL)
                .put("plugged", plugged != 0 || status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL)
                .put("cycleCount", if (Build.VERSION.SDK_INT >= 34) intent.getIntExtra(BatteryManager.EXTRA_CYCLE_COUNT, -1) else JSONObject.NULL)

            val file = File(applicationContext.filesDir, FILE_NAME)
            synchronized(FILE_LOCK) {
                if (file.exists() && file.length() > MAX_FILE_BYTES) {
                    val retained = file.readLines().takeLast(400)
                    file.writeText(retained.joinToString(separator = "\n", postfix = if (retained.isEmpty()) "" else "\n"))
                }
                file.appendText(sample.toString() + "\n")
            }
            AgentTelemetry.record(
                applicationContext,
                "scheduled_battery_sample",
                JSONObject().put("batteryPercent", level * 100 / scale)
            )
            Result.success()
        } catch (ex: Exception) {
            AgentTelemetry.record(applicationContext, "scheduled_battery_sample_failed", JSONObject().put("errorType", ex.javaClass.simpleName))
            Result.retry()
        }
    }

    companion object {
        const val WORK_NAME = "softbox-agent-battery-snapshot"
        const val FILE_NAME = "battery-snapshots.jsonl"
        const val SAMPLE_INTERVAL_MINUTES = 30L
        private const val MAX_FILE_BYTES = 128 * 1024
        private val FILE_LOCK = Any()

        fun schedule(context: Context) {
            val request = androidx.work.PeriodicWorkRequestBuilder<BatterySnapshotWorker>(
                SAMPLE_INTERVAL_MINUTES, TimeUnit.MINUTES
            ).build()
            androidx.work.WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                WORK_NAME,
                androidx.work.ExistingPeriodicWorkPolicy.KEEP,
                request
            )
        }

        fun latest(context: Context): JSONObject? {
            val file = File(context.applicationContext.filesDir, FILE_NAME)
            if (!file.exists()) return null
            return runCatching {
                val line = file.useLines { it.filter(String::isNotBlank).lastOrNull() } ?: return null
                JSONObject(line)
            }.getOrNull()
        }

        fun history(context: Context, limit: Int = 100): List<JSONObject> {
            val file = File(context.applicationContext.filesDir, FILE_NAME)
            if (!file.exists()) return emptyList()
            return runCatching {
                file.useLines { it.filter(String::isNotBlank).toList().takeLast(limit.coerceIn(1, 400)).map(::JSONObject) }
            }.getOrDefault(emptyList())
        }
    }
}
