package com.softbox.phonelab.agent

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.media.AudioManager
import android.media.MediaRecorder
import android.media.ToneGenerator
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.provider.MediaStore
import android.view.Gravity
import android.widget.TextView
import android.widget.Toast
import androidx.core.view.WindowCompat
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.util.concurrent.ConcurrentHashMap

class MainActivity : Activity() {
    private val tests = ConcurrentHashMap<String, JSONObject>()
    private val sensorNames = mutableListOf<String>()
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var server: LocalAgentServer
    private lateinit var resultView: TextView
    private var recorder: MediaRecorder? = null
    private var recordFile: File? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, true)
        setContentView(R.layout.activity_main)
        val deviceInfo = findViewById<TextView>(R.id.deviceInfo)
        resultView = findViewById(R.id.results)
        deviceInfo.text = "${Build.MANUFACTURER} ${Build.MODEL}\nAndroid ${Build.VERSION.RELEASE} • API ${Build.VERSION.SDK_INT}"
        server = LocalAgentServer { createSnapshot() }
        findViewById<TextView>(R.id.bridgeState).text = "USB bridge: local-only 127.0.0.1:27831 • keep this app open"

        findViewById<android.widget.Button>(R.id.testDisplay).setOnClickListener { runDisplayTest() }
        findViewById<android.widget.Button>(R.id.testTouch).setOnClickListener { runTouchTest() }
        findViewById<android.widget.Button>(R.id.testSpeaker).setOnClickListener { runSpeakerTest() }
        findViewById<android.widget.Button>(R.id.testMicrophone).setOnClickListener { runMicrophoneTest() }
        findViewById<android.widget.Button>(R.id.testVibration).setOnClickListener { runVibrationTest() }
        findViewById<android.widget.Button>(R.id.testCamera).setOnClickListener { runCameraTest() }
        findViewById<android.widget.Button>(R.id.testSensors).setOnClickListener { runSensorInventory() }
        renderResults()
    }

    override fun onResume() { super.onResume(); server.start() }
    override fun onPause() { server.stop(); super.onPause() }
    override fun onDestroy() { stopRecorder(); server.stop(); super.onDestroy() }

    private fun saveTest(id: String, status: String, evidence: String) {
        tests[id] = JSONObject()
            .put("status", status)
            .put("evidence", evidence)
            .put("completedAtUtc", Instant.now().toString())
        renderResults()
    }

    private fun renderResults() {
        if (!::resultView.isInitialized) return
        val text = if (tests.isEmpty()) "لم تنفذ اختبارات بعد." else tests.entries
            .sortedBy { it.key }
            .joinToString("\n") { (id, value) -> "$id: ${value.optString("status")} — ${value.optString("evidence")}" }
        resultView.text = text
    }

    private fun runDisplayTest() {
        val panel = TextView(this).apply {
            text = "RED"; textSize = 34f; typeface = Typeface.DEFAULT_BOLD
            gravity = Gravity.CENTER; setTextColor(Color.WHITE); setBackgroundColor(Color.RED)
            setPadding(24, 80, 24, 80)
        }
        val dialog = AlertDialog.Builder(this)
            .setTitle("اختبار ألوان الشاشة")
            .setMessage("ستظهر ألوان أساسية بالتتابع. راجع وجود بقع أو خطوط أو مناطق غير مضيئة.")
            .setView(panel)
            .setPositiveButton("الألوان ظاهرة بشكل طبيعي") { _, _ ->
                saveTest("display", "passed", "المستخدم أكد رؤية الأحمر والأخضر والأزرق؛ فحص بصري ذاتي وليس كشف بكسلات آليًا.")
            }
            .setNegativeButton("يوجد خلل") { _, _ ->
                saveTest("display", "failed", "المستخدم أبلغ عن خلل بصري أثناء اختبار الألوان؛ يلزم فحص فني.")
            }
            .create()
        dialog.show()
        val sequence = listOf(
            Triple("RED", Color.RED, 700L),
            Triple("GREEN", Color.GREEN, 1400L),
            Triple("BLUE", Color.BLUE, 2100L),
            Triple("WHITE", Color.WHITE, 2800L),
            Triple("BLACK", Color.BLACK, 3500L)
        )
        sequence.forEach { (label, color, delay) ->
            handler.postDelayed({
                if (dialog.isShowing) {
                    panel.text = label
                    panel.setTextColor(if (color == Color.WHITE) Color.BLACK else Color.WHITE)
                    panel.setBackgroundColor(color)
                }
            }, delay)
        }
    }

    private fun runTouchTest() {
        lateinit var dialog: AlertDialog
        val grid = TouchGridTestView(this) { count, total ->
            saveTest("touch", "passed", "تم لمس جميع مناطق الشبكة $count/$total. هذا اختبار تغطية تفاعلي وليس قياسًا لقوة الضغط.")
            dialog.dismiss()
        }
        dialog = AlertDialog.Builder(this)
            .setTitle("اختبار تغطية اللمس")
            .setView(grid)
            .setNegativeButton("إلغاء الاختبار") { _, _ ->
                saveTest("touch", "not_tested", "ألغى المستخدم اختبار تغطية اللمس.")
            }
            .create()
        dialog.show()
        dialog.window?.setLayout((resources.displayMetrics.widthPixels * 0.94).toInt(),
            (resources.displayMetrics.heightPixels * 0.82).toInt())
    }

    private fun runSpeakerTest() {
        val tone = ToneGenerator(AudioManager.STREAM_MUSIC, 85)
        tone.startTone(ToneGenerator.TONE_PROP_BEEP, 1400)
        val dialog = AlertDialog.Builder(this)
            .setTitle("اختبار السماعة")
            .setMessage("تم تشغيل نغمة اختبار. هل سمعت الصوت بوضوح من السماعة المطلوبة؟")
            .setPositiveButton("نعم، الصوت واضح") { _, _ ->
                saveTest("speaker", "passed", "أكد المستخدم سماع نغمة الاختبار بوضوح.")
            }
            .setNegativeButton("لا يوجد صوت / الصوت مشوش") { _, _ ->
                saveTest("speaker", "failed", "أبلغ المستخدم عن غياب الصوت أو تشوهه.")
            }
            .create()
        dialog.setOnDismissListener { tone.release() }
        dialog.show()
    }

    private fun runMicrophoneTest() {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO), REQUEST_MIC)
            return
        }
        startMicrophoneRecording()
    }

    private fun startMicrophoneRecording() {
        try {
            val file = File(cacheDir, "phonelab-microphone-test.m4a")
            recordFile = file
            @Suppress("DEPRECATION")
            val media = if (Build.VERSION.SDK_INT >= 31) MediaRecorder(this) else MediaRecorder()
            recorder = media
            media.setAudioSource(MediaRecorder.AudioSource.MIC)
            media.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
            media.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
            media.setOutputFile(file.absolutePath)
            media.prepare()
            media.start()
            Toast.makeText(this, "تحدث الآن لمدة ثانيتين تقريبًا", Toast.LENGTH_SHORT).show()
            handler.postDelayed({
                try {
                    val peak = recorder?.maxAmplitude ?: 0
                    stopRecorder()
                    val status = if (peak > 0) "passed" else "review"
                    saveTest("microphone", status, "Peak amplitude reported by Android recorder: $peak. هذا مؤشر إدخال أولي وليس قياس جودة معاير.")
                } catch (ex: Exception) {
                    stopRecorder()
                    saveTest("microphone", "failed", "تعذر إكمال التسجيل: ${ex.message}")
                }
            }, 2400L)
        } catch (ex: Exception) {
            stopRecorder()
            saveTest("microphone", "failed", "تعذر تهيئة التسجيل: ${ex.message}")
        }
    }

    private fun stopRecorder() {
        try { recorder?.stop() } catch (_: Exception) {}
        try { recorder?.reset() } catch (_: Exception) {}
        try { recorder?.release() } catch (_: Exception) {}
        recorder = null
        recordFile?.delete()
        recordFile = null
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == REQUEST_MIC) {
            if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) startMicrophoneRecording()
            else saveTest("microphone", "not_tested", "لم يمنح المستخدم صلاحية الميكروفون.")
        } else if (requestCode == REQUEST_CAMERA_PERMISSION) {
            if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) runCameraTest()
            else saveTest("camera_inventory", "not_tested", "لم يمنح المستخدم صلاحية الكاميرا.")
        }
    }

    private fun runVibrationTest() {
        try {
            val vibrator = getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
            if (!vibrator.hasVibrator()) {
                saveTest("vibration", "failed", "Android أفاد أن الجهاز لا يحتوي على Vibrator.")
                return
            }
            if (Build.VERSION.SDK_INT >= 26) vibrator.vibrate(VibrationEffect.createOneShot(450, VibrationEffect.DEFAULT_AMPLITUDE))
            else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(450)
            }
            AlertDialog.Builder(this)
                .setTitle("اختبار الاهتزاز")
                .setMessage("هل شعرت بالاهتزاز؟")
                .setPositiveButton("نعم") { _, _ -> saveTest("vibration", "passed", "أكد المستخدم الشعور بنبضة الاهتزاز.") }
                .setNegativeButton("لا") { _, _ -> saveTest("vibration", "failed", "لم يشعر المستخدم بالاهتزاز.") }
                .show()
        } catch (ex: Exception) {
            saveTest("vibration", "failed", "تعذر تشغيل الاهتزاز: ${ex.message}")
        }
    }

    private fun runCameraTest() {
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.CAMERA), REQUEST_CAMERA_PERMISSION)
            return
        }
        try {
            val diagnostics = CameraDiagnostics(this)
            val cameras = diagnostics.describeAll()
            saveTest("camera_inventory", "data_read", "Camera2 اكتشف ${cameras.length()} كاميرا/مسار كاميرا، مع خصائص العدسات المتاحة.")
            diagnostics.runAll(
                onResult = { id, status, evidence -> saveTest(id, status, evidence) },
                onComplete = { saveTest("camera_inventory", "data_read", "اكتمل فحص مسارات Camera2 الظاهرة. راجع نتائج كل كاميرا منفردة.") }
            )
        } catch (ex: Exception) {
            saveTest("camera_inventory", "failed", "تعذر حصر الكاميرات: ${ex.message}")
        }
    }

    private fun runSensorInventory() {
        val manager = getSystemService(Context.SENSOR_SERVICE) as android.hardware.SensorManager
        val found = manager.getSensorList(android.hardware.Sensor.TYPE_ALL)
            .map { "${it.name} (${it.vendor})" }.distinct()
        sensorNames.clear()
        sensorNames.addAll(found)
        saveTest("sensors", "data_read", "تم العثور على ${found.size} مستشعر/مستشعرات معرفة للنظام.")
    }

    private fun createSnapshot(): JSONObject {
        val batteryIntent = registerReceiver(null, android.content.IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        val level = batteryIntent?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1
        val scale = batteryIntent?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: -1
        val percent = if (level >= 0 && scale > 0) (level * 100 / scale) else -1
        val voltage = batteryIntent?.getIntExtra(BatteryManager.EXTRA_VOLTAGE, -1) ?: -1
        val temperatureRaw = batteryIntent?.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, -1) ?: -1
        val cycleCount = if (Build.VERSION.SDK_INT >= 34) batteryIntent?.getIntExtra(BatteryManager.EXTRA_CYCLE_COUNT, -1) ?: -1 else -1
        val batteryManager = getSystemService(Context.BATTERY_SERVICE) as BatteryManager
        val chargeCounter = batteryManager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CHARGE_COUNTER)
        val currentNow = batteryManager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CURRENT_NOW)
        val currentAverage = batteryManager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CURRENT_AVERAGE)
        val energyCounter = batteryManager.getLongProperty(BatteryManager.BATTERY_PROPERTY_ENERGY_COUNTER)
        val cameras = runCatching { CameraDiagnostics(this).describeAll() }.getOrElse { JSONArray() }
        val testObject = JSONObject()
        tests.forEach { (key, value) -> testObject.put(key, value) }
        return JSONObject()
            .put("agentVersion", "0.2.0")
            .put("manufacturer", Build.MANUFACTURER)
            .put("brand", Build.BRAND)
            .put("model", Build.MODEL)
            .put("product", Build.PRODUCT)
            .put("device", Build.DEVICE)
            .put("board", Build.BOARD)
            .put("hardware", Build.HARDWARE)
            .put("bootloader", Build.BOOTLOADER)
            .put("supportedAbis", JSONArray(Build.SUPPORTED_ABIS.toList()))
            .put("androidVersion", Build.VERSION.RELEASE)
            .put("apiLevel", Build.VERSION.SDK_INT)
            .put("batteryPercent", percent)
            .put("batteryVoltageMillivolts", if (voltage >= 0) voltage else JSONObject.NULL)
            .put("batteryTemperatureCelsius", if (temperatureRaw >= 0) temperatureRaw / 10.0 else JSONObject.NULL)
            .put("batteryCycleCount", if (cycleCount >= 0) cycleCount else JSONObject.NULL)
            .put("batteryChargeCounterMicroAh", if (chargeCounter >= 0) chargeCounter else JSONObject.NULL)
            .put("batteryCurrentNowMicroA", if (currentNow != Int.MIN_VALUE) currentNow else JSONObject.NULL)
            .put("batteryCurrentAverageMicroA", if (currentAverage != Int.MIN_VALUE) currentAverage else JSONObject.NULL)
            .put("batteryEnergyCounterNWh", if (energyCounter >= 0L && energyCounter != Long.MIN_VALUE) energyCounter else JSONObject.NULL)
            .put("cameras", cameras)
            .put("sensors", JSONArray(sensorNames))
            .put("tests", testObject)
            .put("capturedAtUtc", Instant.now().toString())
    }

    companion object {
        private const val REQUEST_MIC = 7101
        private const val REQUEST_CAMERA_PERMISSION = 7103
    }
}
