package com.softbox.phonelab.agent

import android.Manifest
import androidx.fragment.app.FragmentActivity
import android.app.AlertDialog
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.wifi.WifiManager
import android.nfc.NfcAdapter
import android.os.Build
import android.os.Bundle
import android.os.Looper
import android.provider.Settings
import android.telephony.TelephonyManager
import androidx.biometric.BiometricManager
import androidx.biometric.BiometricPrompt
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat

/**
 * Device-local hardware checks. The helper never reads IMEI, IMSI, ICCID, phone numbers,
 * Bluetooth addresses, NFC tag UIDs or raw GPS coordinates into the report.
 */
class AgentHardwareDiagnostics(
    private val activity: FragmentActivity,
    private val saveTest: (String, String, String) -> Unit
) {
    private val mainHandler = android.os.Handler(Looper.getMainLooper())
    private var nfcAdapter: NfcAdapter? = null
    private var nfcWaiting = false
    private var gpsManager: LocationManager? = null
    private var gpsListener: LocationListener? = null
    private var gpsWaiting = false

    fun runBluetooth() {
        if (Build.VERSION.SDK_INT >= 31 &&
            !hasPermission(Manifest.permission.BLUETOOTH_CONNECT)) {
            ActivityCompat.requestPermissions(
                activity, arrayOf(Manifest.permission.BLUETOOTH_CONNECT), REQUEST_BLUETOOTH
            )
            return
        }
        try {
            val manager = activity.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager
            val adapter = manager.adapter
            if (adapter == null) {
                saveTest("bluetooth", "not_tested", "لا يكشف Android عن محول Bluetooth لهذا الجهاز.")
                return
            }
            val state = adapter.state
            val label = when (state) {
                android.bluetooth.BluetoothAdapter.STATE_ON -> "ON"
                android.bluetooth.BluetoothAdapter.STATE_OFF -> "OFF"
                android.bluetooth.BluetoothAdapter.STATE_TURNING_ON -> "TURNING_ON"
                android.bluetooth.BluetoothAdapter.STATE_TURNING_OFF -> "TURNING_OFF"
                else -> "UNKNOWN"
            }
            saveTest(
                "bluetooth",
                if (state == android.bluetooth.BluetoothAdapter.STATE_ON) "data_read" else "review",
                "Bluetooth adapter detected; adapter state=$label. لم يتم فحص أجهزة قريبة أو قراءة عناوينها. اربط ملحقًا معروفًا يدويًا من إعدادات Android."
            )
            offerSettings("اختبار Bluetooth", "افتح إعدادات Bluetooth واربط سماعة أو ملحقًا معروفًا، ثم ارجع إلى SoftBox لتسجيل نتيجة الفحص اليدوي.", Settings.ACTION_BLUETOOTH_SETTINGS)
        } catch (ex: SecurityException) {
            saveTest("bluetooth", "not_tested", "رفض Android قراءة حالة Bluetooth: ${ex.message}")
        } catch (ex: Exception) {
            saveTest("bluetooth", "failed", "تعذر فحص Bluetooth: ${ex.message}")
        }
    }

    fun runWifi() {
        try {
            val wifi = activity.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            val connectivity = activity.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
            val network = connectivity.activeNetwork
            val capabilities = if (network != null) connectivity.getNetworkCapabilities(network) else null
            val connectedOverWifi = capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) == true
            val enabled = runCatching { wifi.isWifiEnabled }.getOrDefault(false)
            val state = if (connectedOverWifi) "connected" else if (enabled) "enabled/not-connected" else "disabled"
            saveTest(
                "wifi",
                if (connectedOverWifi) "data_read" else "review",
                "Wi-Fi radio enabled=$enabled; active network over Wi-Fi=$connectedOverWifi; state=$state. لم يتم مسح الشبكات أو حفظ أسماء/كلمات مرور Wi-Fi."
            )
            offerSettings("اختبار Wi-Fi", "افتح إعدادات Wi-Fi واتصل بشبكة اختبار موثوقة، ثم ارجع إلى SoftBox للتحقق من اتصال Wi-Fi.", Settings.ACTION_WIFI_SETTINGS)
        } catch (ex: Exception) {
            saveTest("wifi", "failed", "تعذر قراءة حالة Wi-Fi: ${ex.message}")
        }
    }

    fun runNfc() {
        try {
            val adapter = NfcAdapter.getDefaultAdapter(activity)
            nfcAdapter = adapter
            if (adapter == null) {
                saveTest("nfc", "not_tested", "لا توجد وحدة NFC ظاهرة في Android PackageManager.")
                return
            }
            if (!adapter.isEnabled) {
                saveTest("nfc", "review", "وحدة NFC موجودة لكنها متوقفة. فعّلها من إعدادات NFC ثم أعد الاختبار.")
                offerSettings("NFC غير مفعّل", "افتح إعدادات NFC وفعّلها، ثم ارجع وابدأ الاختبار مرة أخرى.", Settings.ACTION_NFC_SETTINGS)
                return
            }
            nfcWaiting = true
            adapter.enableReaderMode(
                activity,
                { tag ->
                    val technologies = tag.techList.map { it.substringAfterLast('.') }.distinct().sorted()
                    mainHandler.post {
                        if (nfcWaiting) {
                            nfcWaiting = false
                            stopNfcReader()
                            saveTest(
                                "nfc",
                                "passed",
                                "تم اكتشاف بطاقة/وسم NFC فعليًا. التقنيات: ${technologies.joinToString(", ")}. لم يتم حفظ Tag UID أو محتوى الوسم."
                            )
                        }
                    }
                },
                NfcAdapter.FLAG_READER_NFC_A or NfcAdapter.FLAG_READER_NFC_B or
                    NfcAdapter.FLAG_READER_NFC_F or NfcAdapter.FLAG_READER_NFC_V,
                Bundle()
            )
            saveTest("nfc", "review", "NFC جاهز للقراءة. قرّب بطاقة اختبار NFC من ظهر الهاتف خلال 25 ثانية.")
            mainHandler.postDelayed({
                if (nfcWaiting) {
                    nfcWaiting = false
                    stopNfcReader()
                    saveTest("nfc", "not_tested", "لم يتم اكتشاف بطاقة NFC خلال مهلة الاختبار. جرّب موضع الهوائي وبطاقة متوافقة.")
                }
            }, 25_000L)
        } catch (ex: SecurityException) {
            nfcWaiting = false
            saveTest("nfc", "not_tested", "رفض Android تشغيل قارئ NFC: ${ex.message}")
        } catch (ex: Exception) {
            nfcWaiting = false
            saveTest("nfc", "failed", "تعذر تشغيل قارئ NFC: ${ex.message}")
        }
    }

    fun runGps() {
        if (!hasPermission(Manifest.permission.ACCESS_FINE_LOCATION)) {
            ActivityCompat.requestPermissions(
                activity,
                arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION),
                REQUEST_GPS
            )
            return
        }
        try {
            val manager = activity.getSystemService(Context.LOCATION_SERVICE) as LocationManager
            gpsManager = manager
            if (!manager.isProviderEnabled(LocationManager.GPS_PROVIDER)) {
                saveTest("gps", "review", "مزود GPS موجود لكنه متوقف. فعّل الموقع وGPS من إعدادات Android ثم أعد الاختبار.")
                offerSettings("GPS غير مفعّل", "افتح إعدادات الموقع وفعّل خدمات الموقع، ثم ارجع وأعد الاختبار.", Settings.ACTION_LOCATION_SOURCE_SETTINGS)
                return
            }
            if (gpsWaiting) stopGps()
            gpsWaiting = true
            val listener = object : LocationListener {
                override fun onLocationChanged(location: Location) {
                    if (!gpsWaiting) return
                    gpsWaiting = false
                    stopGps()
                    val accuracy = if (location.hasAccuracy()) "%.1f".format(java.util.Locale.US, location.accuracy) + " m" else "unknown"
                    saveTest(
                        "gps",
                        "passed",
                        "استقبل Android إصلاح GPS فعليًا؛ accuracy=$accuracy. لم يتم حفظ خطوط الطول أو العرض أو سجل الموقع."
                    )
                }
                override fun onProviderEnabled(provider: String) {}
                override fun onProviderDisabled(provider: String) {}
            }
            gpsListener = listener
            manager.requestLocationUpdates(LocationManager.GPS_PROVIDER, 1000L, 0f, listener, Looper.getMainLooper())
            saveTest("gps", "review", "بدأ انتظار إصلاح GPS لمدة تصل إلى 30 ثانية. ضع الهاتف قرب نافذة أو في مكان مفتوح.")
            mainHandler.postDelayed({
                if (gpsWaiting) {
                    gpsWaiting = false
                    stopGps()
                    saveTest("gps", "not_tested", "لم يصل إصلاح GPS خلال 30 ثانية. افحص الهوائي والبيئة وإعدادات الموقع.")
                }
            }, 30_000L)
        } catch (ex: SecurityException) {
            gpsWaiting = false
            saveTest("gps", "not_tested", "صلاحية الموقع غير كافية لاختبار GPS: ${ex.message}")
        } catch (ex: Exception) {
            gpsWaiting = false
            stopGps()
            saveTest("gps", "failed", "تعذر تشغيل اختبار GPS: ${ex.message}")
        }
    }

    fun runFingerprint() {
        try {
            val fingerprint = activity.getSystemService(Context.FINGERPRINT_SERVICE) as? android.hardware.fingerprint.FingerprintManager
            if (!activity.packageManager.hasSystemFeature(PackageManager.FEATURE_FINGERPRINT) ||
                fingerprint == null || !fingerprint.isHardwareDetected) {
                saveTest("fingerprint", "not_tested", "لا يعلن الجهاز عن مستشعر بصمة Fingerprint متاح عبر Android.")
                return
            }
            if (!fingerprint.hasEnrolledFingerprints()) {
                saveTest("fingerprint", "review", "مستشعر البصمة موجود لكن لا توجد بصمات مسجلة. يمكن للمالك تسجيل بصمة من إعدادات Android ثم إعادة الاختبار.")
                offerSettings("لا توجد بصمة مسجلة", "افتح إعدادات الأمان وسجّل بصمة للمالك، ثم ارجع وأعد الاختبار.", Settings.ACTION_SECURITY_SETTINGS)
                return
            }
            val biometricManager = BiometricManager.from(activity)
            val capability = biometricManager.canAuthenticate(BiometricManager.Authenticators.BIOMETRIC_STRONG)
            if (capability != BiometricManager.BIOMETRIC_SUCCESS) {
                saveTest("fingerprint", "review", "مستشعر البصمة موجود وبصمة مسجلة، لكن Android لا يسمح بالمصادقة حاليًا. رمز الحالة=$capability")
                return
            }
            val prompt = BiometricPrompt(
                activity,
                ContextCompat.getMainExecutor(activity),
                object : BiometricPrompt.AuthenticationCallback() {
                    override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                        saveTest("fingerprint", "passed", "نجحت مصادقة BIOMETRIC_STRONG من واجهة Android النظامية. لم يصل SoftBox إلى بيانات البصمة.")
                    }
                    override fun onAuthenticationFailed() {
                        saveTest("fingerprint", "review", "لم تتطابق المحاولة الحالية. يمكن إعادة المحاولة من نافذة Android النظامية.")
                    }
                    override fun onAuthenticationError(errorCode: Int, errString: CharSequence) {
                        if (errorCode != BiometricPrompt.ERROR_USER_CANCELED &&
                            errorCode != BiometricPrompt.ERROR_NEGATIVE_BUTTON) {
                            saveTest("fingerprint", "review", "أوقفت Android المصادقة: $errString")
                        }
                    }
                }
            )
            val info = BiometricPrompt.PromptInfo.Builder()
                .setTitle("اختبار مستشعر البصمة")
                .setSubtitle("ضع بصمتك المسجلة على مستشعر الهاتف. تتم المصادقة داخل Android.")
                .setNegativeButtonText("إلغاء")
                .setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG)
                .build()
            prompt.authenticate(info)
        } catch (ex: SecurityException) {
            saveTest("fingerprint", "not_tested", "تعذر الوصول إلى واجهة المصادقة الحيوية: ${ex.message}")
        } catch (ex: Exception) {
            saveTest("fingerprint", "failed", "تعذر تشغيل اختبار البصمة: ${ex.message}")
        }
    }

    fun runSim() {
        if (!hasPermission(Manifest.permission.READ_PHONE_STATE)) {
            ActivityCompat.requestPermissions(activity, arrayOf(Manifest.permission.READ_PHONE_STATE), REQUEST_SIM)
            return
        }
        try {
            val telephony = activity.getSystemService(Context.TELEPHONY_SERVICE) as TelephonyManager
            val state = when (telephony.simState) {
                TelephonyManager.SIM_STATE_ABSENT -> "ABSENT"
                TelephonyManager.SIM_STATE_READY -> "READY"
                TelephonyManager.SIM_STATE_PIN_REQUIRED -> "PIN_REQUIRED"
                TelephonyManager.SIM_STATE_PUK_REQUIRED -> "PUK_REQUIRED"
                TelephonyManager.SIM_STATE_NETWORK_LOCKED -> "NETWORK_LOCKED"
                TelephonyManager.SIM_STATE_NOT_READY -> "NOT_READY"
                TelephonyManager.SIM_STATE_PERM_DISABLED -> "PERM_DISABLED"
                TelephonyManager.SIM_STATE_CARD_IO_ERROR -> "CARD_IO_ERROR"
                TelephonyManager.SIM_STATE_CARD_RESTRICTED -> "CARD_RESTRICTED"
                else -> "UNKNOWN"
            }
            val operator = runCatching { telephony.networkOperatorName }.getOrDefault("").take(80)
            val modemCount = if (Build.VERSION.SDK_INT >= 30) telephony.activeModemCount else 1
            saveTest(
                "sim",
                if (telephony.simState == TelephonyManager.SIM_STATE_READY) "data_read" else "review",
                "SIM state=$state; active modem count=$modemCount; operator=$operator. لم تتم قراءة رقم الهاتف أو IMEI أو IMSI أو ICCID. حالة التسجيل لا تحسم أهلية فتح الشبكة."
            )
            offerSettings("فحص شريحة SIM", "راجع حالة الشريحة في إعدادات الشبكة، ثم جرّب مكالمة صادرة وشريحة أخرى بموافقة صاحبها. لا يغيّر SoftBox قفل المشغّل.", Settings.ACTION_NETWORK_OPERATOR_SETTINGS)
        } catch (ex: SecurityException) {
            saveTest("sim", "not_tested", "رفض Android قراءة حالة SIM: ${ex.message}")
        } catch (ex: Exception) {
            saveTest("sim", "failed", "تعذر فحص SIM: ${ex.message}")
        }
    }

    fun handlePermissionResult(requestCode: Int, granted: Boolean): Boolean {
        if (requestCode !in setOf(REQUEST_BLUETOOTH, REQUEST_GPS, REQUEST_SIM)) return false
        if (!granted) {
            val id = when (requestCode) {
                REQUEST_BLUETOOTH -> "bluetooth"
                REQUEST_GPS -> "gps"
                else -> "sim"
            }
            saveTest(id, "not_tested", "لم يمنح المستخدم صلاحية Android المطلوبة للاختبار.")
            return true
        }
        when (requestCode) {
            REQUEST_BLUETOOTH -> runBluetooth()
            REQUEST_GPS -> runGps()
            REQUEST_SIM -> runSim()
        }
        return true
    }

    fun stop() {
        nfcWaiting = false
        stopNfcReader()
        gpsWaiting = false
        stopGps()
    }

    private fun stopNfcReader() {
        try { nfcAdapter?.disableReaderMode(activity) } catch (_: Exception) {}
    }

    private fun stopGps() {
        val listener = gpsListener
        if (listener != null) {
            try { gpsManager?.removeUpdates(listener) } catch (_: Exception) {}
        }
        gpsListener = null
    }

    private fun hasPermission(permission: String): Boolean
        = ContextCompat.checkSelfPermission(activity, permission) == PackageManager.PERMISSION_GRANTED

    private fun offerSettings(title: String, message: String, action: String) {
        AlertDialog.Builder(activity)
            .setTitle(title)
            .setMessage(message)
            .setPositiveButton("فتح إعدادات Android") { _, _ ->
                try {
                    activity.startActivity(Intent(action))
                } catch (ex: Exception) {
                    saveTest("hardware_settings", "review", "تعذر فتح إعدادات Android: ${ex.message}")
                }
            }
            .setNegativeButton("لاحقًا", null)
            .show()
    }

    companion object {
        private const val REQUEST_BLUETOOTH = 7201
        private const val REQUEST_GPS = 7202
        private const val REQUEST_SIM = 7203
    }
}
