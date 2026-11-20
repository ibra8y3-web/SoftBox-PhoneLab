package com.softbox.phonelab.agent

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.app.Dialog
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.media.AudioManager
import android.media.MediaRecorder
import android.media.ToneGenerator
import android.hardware.camera2.CameraManager
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.provider.MediaStore
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.widget.TextView
import android.widget.Button
import android.widget.LinearLayout
import android.widget.Toast
import androidx.core.view.WindowCompat
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CopyOnWriteArrayList

class MainActivity : Activity() {
    private val tests = ConcurrentHashMap<String, JSONObject>()
    private val sensorNames = CopyOnWriteArrayList<String>()
    private val hardwareFeatures = CopyOnWriteArrayList<String>()
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var server: LocalAgentServer
    private lateinit var resultView: TextView
    private var recorder: MediaRecorder? = null
    private var recordFile: File? = null
    private var connectionAllowed = false
    private var consentDialog: AlertDialog? = null
    private lateinit var bridgeStateView: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, true)
        setContentView(R.layout.activity_main)
        val deviceInfo = findViewById<TextView>(R.id.deviceInfo)
        resultView = findViewById(R.id.results)
        deviceInfo.text = "${Build.MANUFACTURER} ${Build.MODEL}\nAndroid ${Build.VERSION.RELEASE} • API ${Build.VERSION.SDK_INT}"
        hardwareFeatures.addAll(packageManager.systemAvailableFeatures.mapNotNull { it.name }.filter { it.isNotBlank() }.distinct().sorted())
        server = LocalAgentServer({ createSnapshot() }, { command -> dispatchRemoteCommand(command) })
        bridgeStateView = findViewById(R.id.bridgeState)
        bridgeStateView.text = "في انتظار موافقتك على اتصال USB من SoftBox PhoneLab."

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
        requestConnectionConsent()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        requestConnectionConsent()
    }

    override fun onResume() {
        super.onResume()
        if (connectionAllowed) {
            server.start()
            bridgeStateView.text = "اتصال USB مصرح • 127.0.0.1:27831"
        }
    }

    override fun onPause() {
        server.stop()
        super.onPause()
    }

    private fun requestConnectionConsent() {
        connectionAllowed = false
        server.stop()
        consentDialog?.dismiss()
        bridgeStateView.text = "في انتظار موافقتك على اتصال USB من SoftBox PhoneLab."
        consentDialog = AlertDialog.Builder(this)
            .setTitle("طلب اتصال من SoftBox PhoneLab")
            .setMessage(
                "يوجد كمبيوتر متصل بهذا الهاتف عبر USB ويطلب تشغيل PhoneLab Agent.\\n\\n" +
                "عند السماح، سيتمكن برنامج PhoneLab على هذا الكمبيوتر من قراءة معلومات التشخيص وطلب الاختبارات التي تختار تشغيلها، مثل الشاشة واللمس والصوت والكاميرا والحساسات.\\n\\n" +
                "الاتصال محلي عبر USB وADB على 127.0.0.1 فقط؛ لا يرفع Agent بياناتك إلى خادم خارجي. يمكنك رفض الطلب، ولن يبدأ استقبال أوامر الاختبار."
            )
            .setPositiveButton("السماح لهذا الاتصال") { _, _ ->
                connectionAllowed = true
                bridgeStateView.text = "اتصال USB مصرح • 127.0.0.1:27831"
                server.start()
            }
            .setNegativeButton("رفض الاتصال") { _, _ ->
                connectionAllowed = false
                bridgeStateView.text = "تم رفض اتصال الكمبيوتر. لن تُستقبل أوامر PhoneLab."
                server.stop()
            }
            .setCancelable(false)
            .create()
            .also { it.show() }
    }
    override fun onDestroy() { stopRecorder(); server.stop(); super.onDestroy() }

    private fun dispatchRemoteCommand(command: String): Boolean {
        val allowed = setOf("display", "touch", "speaker", "earpiece", "microphone", "flashlight", "vibration", "camera", "sensors", "hardware", "battery_capacity_start", "battery_capacity_finish")
        if (command !in allowed) return false
        handler.post {
            when (command) {
                "display" -> runDisplayTest()
                "touch" -> runTouchTest()
                "speaker" -> runSpeakerTest()
                "earpiece" -> runEarpieceTest()
                "microphone" -> runMicrophoneTest()
                "flashlight" -> runFlashlightTest()
                "vibration" -> runVibrationTest()
                "camera" -> runCameraTest()
                "sensors" -> runSensorInventory()
                "hardware" -> runHardwareInventory()
                "battery_capacity_start" -> startBatteryCapacityObservation()
                "battery_capacity_finish" -> finishBatteryCapacityObservation()
            }
        }
        return true
    }
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
        val dialog = Dialog(this)
        val root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setBackgroundColor(Color.BLACK); setPadding(18,18,18,18) }
        val label = TextView(this).apply { textSize = 28f; typeface = Typeface.DEFAULT_BOLD; gravity = Gravity.CENTER; setTextColor(Color.WHITE); layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f) }
        val controls = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER }
        val next = Button(this).apply { text = "اللون التالي" }
        val pass = Button(this).apply { text = "الشاشة طبيعية"; isEnabled = false }
        val fail = Button(this).apply { text = "يوجد عيب" }
        controls.addView(next, LinearLayout.LayoutParams(0,58,1f)); controls.addView(pass, LinearLayout.LayoutParams(0,58,1f)); controls.addView(fail, LinearLayout.LayoutParams(0,58,1f))
        root.addView(label); root.addView(controls); dialog.setContentView(root); dialog.setCancelable(true)
        val patterns = listOf(
            Triple("أحمر", Color.RED, Color.WHITE), Triple("أخضر", Color.GREEN, Color.BLACK), Triple("أزرق", Color.BLUE, Color.WHITE),
            Triple("أبيض", Color.WHITE, Color.BLACK), Triple("أسود", Color.BLACK, Color.WHITE), Triple("رمادي 25%", Color.rgb(64,64,64), Color.WHITE),
            Triple("رمادي 50%", Color.rgb(128,128,128), Color.BLACK), Triple("رمادي 75%", Color.rgb(192,192,192), Color.BLACK),
            Triple("سماوي", Color.CYAN, Color.BLACK), Triple("أرجواني", Color.MAGENTA, Color.WHITE)
        )
        var index = 0
        fun showPattern() {
            val item = patterns[index]
            root.setBackgroundColor(item.second)
            label.text = item.first + "\n" + (index + 1) + " / " + patterns.size + "\nافحص البقع والخطوط وتفاوت الإضاءة"
            label.setTextColor(item.third)
            next.isEnabled = index < patterns.lastIndex
            pass.isEnabled = index == patterns.lastIndex
        }
        next.setOnClickListener { if (index < patterns.lastIndex) { index++; showPattern() } }
        pass.setOnClickListener { saveTest("display", "passed", "أكمل المستخدم تسلسل 10 أنماط RGB وأبيض وأسود وتدرج رمادي وأكد عدم ملاحظة عيب. فحص بصري ذاتي."); dialog.dismiss() }
        fail.setOnClickListener { saveTest("display", "failed", "أبلغ المستخدم عن بقع أو خطوط أو بكسلات أو مناطق غير طبيعية خلال تسلسل الألوان."); dialog.dismiss() }
        dialog.setOnCancelListener { saveTest("display", "not_tested", "ألغى المستخدم اختبار ألوان الشاشة قبل إنهائه.") }
        dialog.show()
        dialog.window?.setLayout(WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT)
        dialog.window?.setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN, WindowManager.LayoutParams.FLAG_FULLSCREEN)
        dialog.window?.decorView?.systemUiVisibility = View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or View.SYSTEM_UI_FLAG_FULLSCREEN or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
        showPattern()
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
        } else if (requestCode == REQUEST_FLASH_PERMISSION) {
            if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) runFlashlightTest()
            else saveTest("flashlight", "not_tested", "لم يمنح المستخدم صلاحية الكاميرا لتشغيل الفلاش.")
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

    private fun runEarpieceTest() {
        val tone = ToneGenerator(AudioManager.STREAM_VOICE_CALL, 85)
        tone.startTone(ToneGenerator.TONE_PROP_BEEP, 1200)
        val dialog = AlertDialog.Builder(this).setTitle("اختبار سماعة المكالمات")
            .setMessage("تم تشغيل نغمة من قناة صوت المكالمات. هل سمعتها من سماعة الأذن العلوية؟")
            .setPositiveButton("نعم، سمعتها") { _, _ -> saveTest("earpiece", "passed", "أكد المستخدم سماع النغمة من قناة سماعة المكالمات.") }
            .setNegativeButton("لا يوجد صوت") { _, _ -> saveTest("earpiece", "failed", "أبلغ المستخدم عن عدم سماع نغمة سماعة المكالمات.") }.create()
        dialog.setOnDismissListener { tone.release() }; dialog.show()
    }

    private fun runFlashlightTest() {
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) { requestPermissions(arrayOf(Manifest.permission.CAMERA), REQUEST_FLASH_PERMISSION); return }
        try {
            val cameraManager = getSystemService(Context.CAMERA_SERVICE) as CameraManager
            val id = cameraManager.cameraIdList.firstOrNull { cameraManager.getCameraCharacteristics(it).get(android.hardware.camera2.CameraCharacteristics.FLASH_INFO_AVAILABLE) == true }
            if (id == null) { saveTest("flashlight", "not_tested", "لا يكشف Android عن وحدة فلاش LED قابلة للتحكم لهذا الجهاز."); return }
            cameraManager.setTorchMode(id, true)
            handler.postDelayed({
                try { cameraManager.setTorchMode(id, false) } catch (_: Exception) {}
                AlertDialog.Builder(this).setTitle("اختبار فلاش LED").setMessage("تم تشغيل الفلاش لمدة قصيرة. هل أضاء الفلاش فعليًا؟")
                    .setPositiveButton("نعم، أضاء") { _, _ -> saveTest("flashlight", "passed", "أكد المستخدم إضاءة الفلاش باستخدام CameraManager.setTorchMode.") }
                    .setNegativeButton("لم يضئ") { _, _ -> saveTest("flashlight", "failed", "أبلغ المستخدم أن الفلاش لم يضئ أثناء أمر التشغيل.") }
                    .setOnCancelListener { saveTest("flashlight", "not_tested", "لم يؤكد المستخدم نتيجة الفلاش.") }.show()
            }, 650L)
        } catch (ex: Exception) { saveTest("flashlight", "review", "تعذر تشغيل الفلاش: " + ex.message) }
    }

    private fun runHardwareInventory() {
        val found = packageManager.systemAvailableFeatures.mapNotNull { it.name }.filter { it.isNotBlank() }.distinct().sorted()
        hardwareFeatures.clear(); hardwareFeatures.addAll(found)
        val cameraIds = runCatching { (getSystemService(Context.CAMERA_SERVICE) as CameraManager).cameraIdList.size }.getOrDefault(0)
        val evidence = "System features: " + found.size + "; Camera2 IDs: " + cameraIds + "; NFC=" + packageManager.hasSystemFeature(PackageManager.FEATURE_NFC) +
            "; Fingerprint=" + packageManager.hasSystemFeature(PackageManager.FEATURE_FINGERPRINT) + "; GPS=" + packageManager.hasSystemFeature(PackageManager.FEATURE_LOCATION_GPS) + "."
        saveTest("hardware_features", "data_read", evidence)
    }

    private fun runSensorInventory() {
        val manager = getSystemService(Context.SENSOR_SERVICE) as SensorManager
        val found = manager.getSensorList(Sensor.TYPE_ALL)
        sensorNames.clear(); sensorNames.addAll(found.map { "${it.name} (${it.vendor})" }.distinct())
        val sampled = ConcurrentHashMap<Int, String>()
        val listener = object : SensorEventListener {
            override fun onSensorChanged(event: SensorEvent) {
                val values = event.values.take(3).joinToString(", ") { "%.3f".format(java.util.Locale.US, it) }
                sampled[event.sensor.type] = event.sensor.name + ": " + values
            }
            override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {}
        }
        val registered = found.filter { manager.registerListener(listener, it, SensorManager.SENSOR_DELAY_NORMAL) }
        handler.postDelayed({
            manager.unregisterListener(listener)
            val sampleText = sampled.values.sorted().take(14).joinToString(" | ")
            val status = if (sampled.isNotEmpty()) "data_read" else "review"
            saveTest("sensors", status, "Sensors present: " + found.size + "; event sources: " + sampled.size + "/" + registered.size + ". " +
                (if (sampleText.isBlank()) "لم تصل قراءات أحداث خلال فترة الرصد." else sampleText) + " وصول حدث يثبت القراءة فقط ولا يعاير دقة الحساس.")
        }, 2800L)
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
            .put("agentVersion", "0.5.0")
            .put("manufacturer", Build.MANUFACTURER)
            .put("brand", Build.BRAND)
            .put("model", Build.MODEL)
            .put("product", Build.PRODUCT)
            .put("device", Build.DEVICE)
            .put("board", Build.BOARD)
            .put("hardware", Build.HARDWARE)
            .put("bootloader", Build.BOOTLOADER)
            .put("supportedAbis", JSONArray(Build.SUPPORTED_ABIS.toList()))
            .put("hardwareFeatures", JSONArray(hardwareFeatures.toList()))
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
        private const val REQUEST_FLASH_PERMISSION = 7104
    }
}
