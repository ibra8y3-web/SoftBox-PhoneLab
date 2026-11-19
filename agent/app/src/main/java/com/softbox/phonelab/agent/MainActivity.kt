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
        findViewById<android.widget.Button>(R.id.startBatteryCapacity).setOnClickListener { startBatteryCapacityObservation() }
        findViewById<android.widget.Button>(R.id.finishBatteryCapacity).setOnClickListener { finishBatteryCapacityObservation() }
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


    private data class BatteryObservation(
        val percent: Int,
        val chargeCounterMicroAh: Int,
        val currentNowMicroA: Int,
        val currentAverageMicroA: Int,
        val voltageMillivolts: Int,
        val temperatureCelsius: Double?,
        val isPluggedIn: Boolean
    )

    private fun readBatteryObservation(): BatteryObservation? {
        val intent = registerReceiver(null, android.content.IntentFilter(Intent.ACTION_BATTERY_CHANGED)) ?: return null
        val level = intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = intent.getIntExtra(BatteryManager.EXTRA_SCALE, -1)
        if (level < 0 || scale <= 0) return null
        val manager = getSystemService(Context.BATTERY_SERVICE) as BatteryManager
        val charge = manager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CHARGE_COUNTER)
        if (charge < 0) return null
        val temperature = intent.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, -1)
        return BatteryObservation(
            percent = level * 100 / scale,
            chargeCounterMicroAh = charge,
            currentNowMicroA = manager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CURRENT_NOW),
            currentAverageMicroA = manager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CURRENT_AVERAGE),
            voltageMillivolts = intent.getIntExtra(BatteryManager.EXTRA_VOLTAGE, -1),
            temperatureCelsius = if (temperature >= 0) temperature / 10.0 else null,
            isPluggedIn = intent.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0 ||
                intent.getIntExtra(BatteryManager.EXTRA_STATUS, BatteryManager.BATTERY_STATUS_UNKNOWN) in
                    listOf(BatteryManager.BATTERY_STATUS_CHARGING, BatteryManager.BATTERY_STATUS_FULL)
        )
    }

    private fun startBatteryCapacityObservation() {
        val sample = readBatteryObservation()
        if (sample == null) {
            saveTest("battery_capacity", "not_tested", "هذا الهاتف لا يعرض Charge Counter صالحًا من Android؛ استخدم محلل بطارية خارجيًا.")
            return
        }
        if (sample.isPluggedIn) {
            saveTest("battery_capacity", "review", "افصل الشاحن قبل بدء الرصد؛ لا يمكن تقدير التفريغ أثناء توصيل الهاتف بالطاقة.")
            return
        }
        if (sample.temperatureCelsius != null && sample.temperatureCelsius >= 43.0) {
            saveTest("battery_capacity", "review", "حرارة البطارية الحالية ${sample.temperatureCelsius}°C. اترك الهاتف يبرد قبل بدء الرصد.")
            return
        }
        val preferences = getSharedPreferences("phonelab_battery_capacity", Context.MODE_PRIVATE)
        preferences.edit()
            .remove("capacity_estimate_tenth_mah")
            .remove("capacity_estimate_at_epoch_ms")
            .putInt("start_percent", sample.percent)
            .putInt("start_charge_micro_ah", sample.chargeCounterMicroAh)
            .putInt("start_current_micro_a", sample.currentNowMicroA)
            .putLong("start_at_epoch_ms", System.currentTimeMillis())
            .apply()
        saveTest(
            "battery_capacity",
            "data_read",
            "تم حفظ نقطة البداية: " + sample.percent + "%، Charge Counter " +
                sample.chargeCounterMicroAh + " µAh. استخدم الهاتف بشكل طبيعي/تحت حملك المعتاد، ولا توصله بالشاحن. أنهِ الرصد بعد انخفاض 10 نقاط مئوية على الأقل."
        )
        AlertDialog.Builder(this)
            .setTitle("بدء رصد سعة البطارية")
            .setMessage("تم حفظ نقطة البداية. استخدم الهاتف كما تستخدمه عادةً تحت حملك المعتاد، ثم عد واضغط إنهاء الرصد بعد انخفاض الشحن 10% على الأقل. لا يلزم تشغيل حمل اصطناعي أو إجهاد الهاتف.")
            .setPositiveButton("حسنًا", null)
            .show()
    }

    private fun finishBatteryCapacityObservation() {
        val preferences = getSharedPreferences("phonelab_battery_capacity", Context.MODE_PRIVATE)
        if (!preferences.contains("start_at_epoch_ms")) {
            saveTest("battery_capacity", "not_tested", "لا توجد نقطة بداية محفوظة. اضغط بدء رصد سعة البطارية أولًا.")
            return
        }
        val sample = readBatteryObservation()
        if (sample == null) {
            saveTest("battery_capacity", "review", "تعذر قراءة Charge Counter في نهاية الرصد. نقطة البداية ما زالت محفوظة لإعادة المحاولة.")
            return
        }
        if (sample.isPluggedIn) {
            saveTest("battery_capacity", "review", "الهاتف متصل بالشاحن الآن؛ افصله ثم أعد إنهاء الرصد. نقطة البداية ما زالت محفوظة.")
            return
        }
        if (sample.temperatureCelsius != null && sample.temperatureCelsius >= 45.0) {
            saveTest("battery_capacity", "review", "حرارة البطارية ${sample.temperatureCelsius}°C مرتفعة لاعتبار العينة صالحة. اترك الهاتف يبرد ثم أعد المحاولة.")
            return
        }

        val startPercent = preferences.getInt("start_percent", -1)
        val startCharge = preferences.getInt("start_charge_micro_ah", -1)
        val startCurrent = preferences.getInt("start_current_micro_a", Int.MIN_VALUE)
        val startAt = preferences.getLong("start_at_epoch_ms", 0L)
        val elapsedMinutes = ((System.currentTimeMillis() - startAt).coerceAtLeast(0L) / 60000L)
        val dischargedPercent = startPercent - sample.percent
        val dischargedMicroAh = startCharge - sample.chargeCounterMicroAh

        if (elapsedMinutes < 15 || dischargedPercent < 10 || dischargedMicroAh <= 0) {
            saveTest(
                "battery_capacity",
                "review",
                "العينة غير كافية بعد: المدة " + elapsedMinutes + " دقيقة، فرق الشحن " +
                    dischargedPercent + "%، فرق Charge Counter " + dischargedMicroAh +
                    " µAh. اترك نقطة البداية محفوظة، استخدم الهاتف دون شحن، ثم أعد المحاولة بعد 10% تفريغ و15 دقيقة على الأقل."
            )
            return
        }

        val estimatedMah = dischargedMicroAh * 100.0 / dischargedPercent / 1000.0
        if (!estimatedMah.isFinite() || estimatedMah !in 250.0..20000.0) {
            saveTest("battery_capacity", "review", "قيمة تقدير السعة خارج النطاق المتوقع: " + estimatedMah + " mAh. لا تعتمد عليها؛ افحص Fuel Gauge بمحلل بطارية.")
            return
        }

        preferences.edit()
            .putLong("capacity_estimate_tenth_mah", (estimatedMah * 10.0).toLong())
            .putLong("capacity_estimate_at_epoch_ms", System.currentTimeMillis())
            .remove("start_percent")
            .remove("start_charge_micro_ah")
            .remove("start_current_micro_a")
            .remove("start_at_epoch_ms")
            .apply()

        val startCurrentText = if (startCurrent == Int.MIN_VALUE) "غير متاح" else startCurrent.toString() + " µA"
        val endCurrentText = if (sample.currentNowMicroA == Int.MIN_VALUE) "غير متاح" else sample.currentNowMicroA.toString() + " µA"
        saveTest(
            "battery_capacity",
            "review",
            "تقدير Fuel Gauge ≈ " + "%.0f".format(java.util.Locale.US, estimatedMah) +
                " mAh بعد " + elapsedMinutes + " دقيقة وتفريغ " + dischargedPercent +
                "%. التيار اللحظي: " + startCurrentText + " → " + endCurrentText +
                ". هذا تقدير رصدي تحت استخدامك الفعلي، وليس اختبار سعة معايرًا أو قياس مقاومة داخلية."
        )
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
        val energyCounter = if (Build.VERSION.SDK_INT >= 28)
            batteryManager.getLongProperty(BatteryManager.BATTERY_PROPERTY_ENERGY_COUNTER) else Long.MIN_VALUE
        val cameras = runCatching { CameraDiagnostics(this).describeAll() }.getOrElse { JSONArray() }
        val testObject = JSONObject()
        tests.forEach { (key, value) -> testObject.put(key, value) }
        return JSONObject()
            .put("agentVersion", "0.3.0")
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
            .put("batteryCapacityEstimateMah", run {
                val tenths = getSharedPreferences("phonelab_battery_capacity", Context.MODE_PRIVATE)
                    .getLong("capacity_estimate_tenth_mah", 0L)
                if (tenths > 0L) tenths / 10.0 else JSONObject.NULL
            })
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
