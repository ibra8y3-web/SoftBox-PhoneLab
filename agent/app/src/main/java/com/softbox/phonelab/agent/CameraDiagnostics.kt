package com.softbox.phonelab.agent

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.ImageFormat
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CameraManager
import android.hardware.camera2.CaptureRequest
import android.hardware.camera2.params.OutputConfiguration
import android.hardware.camera2.params.SessionConfiguration
import android.media.ImageReader
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.Executor

/** Camera2 inventory and real JPEG capture for each camera route exposed by Android. */
class CameraDiagnostics(private val activity: Activity) {
    private val manager = activity.getSystemService(Context.CAMERA_SERVICE) as CameraManager
    data class CameraTarget(val id: String, val parentId: String, val physicalId: String?)

    fun describeAll(): JSONArray {
        val available = manager.cameraIdList.toSet()
        val output = JSONArray()
        manager.cameraIdList.forEach { logicalId ->
            val c = manager.getCameraCharacteristics(logicalId)
            output.put(describe(logicalId, c, logicalId, null, false))
            if (Build.VERSION.SDK_INT >= 28) {
                c.physicalCameraIds.filter { it !in available }.forEach { physicalId ->
                    output.put(describe(physicalId, c, logicalId, physicalId, true))
                }
            }
        }
        return output
    }

    fun runAll(onResult: (String, String, String) -> Unit, onComplete: () -> Unit) {
        if (activity.checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            onResult("camera", "not_tested", "لم يتم منح صلاحية الكاميرا.")
            onComplete()
            return
        }
        val targets = mutableListOf<CameraTarget>()
        val available = manager.cameraIdList.toSet()
        manager.cameraIdList.forEach { id ->
            val c = manager.getCameraCharacteristics(id)
            targets.add(CameraTarget(id, id, null))
            if (Build.VERSION.SDK_INT >= 28) {
                c.physicalCameraIds.filter { it !in available }.forEach { physical ->
                    targets.add(CameraTarget(physical, id, physical))
                }
            }
        }
        if (targets.isEmpty()) {
            onResult("camera", "failed", "لم يعرض Camera2 أي كاميرا داخلية.")
            onComplete()
            return
        }
        testNext(targets, 0, onResult, onComplete)
    }

    private fun testNext(targets: List<CameraTarget>, index: Int, onResult: (String, String, String) -> Unit, onComplete: () -> Unit) {
        if (index >= targets.size) { onComplete(); return }
        val target = targets[index]
        captureOne(target) { status, evidence ->
            onResult("camera_" + safeKey(target.id), status, evidence)
            testNext(targets, index + 1, onResult, onComplete)
        }
    }

    private fun captureOne(target: CameraTarget, completed: (String, String) -> Unit) {
        val thread = HandlerThread("PhoneLab-Camera2-" + target.id).apply { start() }
        val handler = Handler(thread.looper)
        val executor = Executor { command -> handler.post(command) }
        var camera: CameraDevice? = null
        var session: CameraCaptureSession? = null
        var reader: ImageReader? = null
        val lock = Any()
        var finished = false

        fun finish(status: String, evidence: String) {
            val proceed = synchronized(lock) { if (finished) false else { finished = true; true } }
            if (!proceed) return
            try { session?.close() } catch (_: Exception) {}
            try { camera?.close() } catch (_: Exception) {}
            try { reader?.close() } catch (_: Exception) {}
            try { thread.quitSafely() } catch (_: Exception) {}
            activity.runOnUiThread { completed(status, evidence) }
        }

        handler.postDelayed({ finish("review", "انتهت مهلة فتح/التقاط الكاميرا " + target.id + ".") }, 12000L)
        try {
            val characteristics = manager.getCameraCharacteristics(target.parentId)
            val map = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP)
            val sizes = map?.getOutputSizes(ImageFormat.JPEG).orEmpty()
            val size = sizes.filter { it.width <= 1920 && it.height <= 1080 }
                .maxByOrNull { it.width.toLong() * it.height.toLong() }
                ?: sizes.maxByOrNull { it.width.toLong() * it.height.toLong() }
                ?: android.util.Size(640, 480)
            reader = ImageReader.newInstance(size.width, size.height, ImageFormat.JPEG, 2)
            val activeReader = reader!!
            activeReader.setOnImageAvailableListener({ source ->
                val image = try { source.acquireLatestImage() } catch (_: Exception) { null }
                val bytes = try { image?.planes?.firstOrNull()?.buffer?.remaining() ?: 0 } catch (_: Exception) { 0 }
                try { image?.close() } catch (_: Exception) {}
                if (bytes > 0) finish("passed", "Camera2 فتح " + target.id + " والتقط JPEG فعليًا (" + size.width + "x" + size.height + ", " + bytes + " bytes).")
                else finish("failed", "Camera2 فتح " + target.id + " لكنه لم ينتج إطار صورة.")
            }, handler)

            manager.openCamera(target.parentId, object : CameraDevice.StateCallback() {
                override fun onOpened(device: CameraDevice) {
                    camera = device
                    try {
                        val callback = object : CameraCaptureSession.StateCallback() {
                            override fun onConfigured(configured: CameraCaptureSession) {
                                session = configured
                                try {
                                    val request = device.createCaptureRequest(CameraDevice.TEMPLATE_STILL_CAPTURE)
                                    request.addTarget(activeReader.surface)
                                    request.set(CaptureRequest.CONTROL_MODE, CaptureRequest.CONTROL_MODE_AUTO)
                                    configured.capture(request.build(), null, handler)
                                } catch (ex: Exception) {
                                    finish("failed", "فشل التقاط الكاميرا " + target.id + ": " + ex.message)
                                }
                            }
                            override fun onConfigureFailed(failed: CameraCaptureSession) {
                                finish("failed", "فشل إعداد جلسة التقاط الكاميرا " + target.id + ".")
                            }
                        }
                        if (target.physicalId != null && Build.VERSION.SDK_INT >= 28) {
                            val output = OutputConfiguration(activeReader.surface).apply { setPhysicalCameraId(target.physicalId) }
                            val config = SessionConfiguration(SessionConfiguration.SESSION_REGULAR, listOf(output), executor, callback)
                            device.createCaptureSession(config)
                        } else {
                            @Suppress("DEPRECATION")
                            device.createCaptureSession(listOf(activeReader.surface), callback, handler)
                        }
                    } catch (ex: Exception) {
                        finish("review", "الموديل لا يسمح بمسار التقاط مستقل للكاميرا " + target.id + ": " + ex.message)
                    }
                }
                override fun onDisconnected(device: CameraDevice) { finish("failed", "انقطع اتصال Camera2 أثناء اختبار " + target.id + ".") }
                override fun onError(device: CameraDevice, error: Int) { finish("failed", "Camera2 أعاد خطأ " + error + " للكاميرا " + target.id + ".") }
            }, handler)
        } catch (ex: SecurityException) {
            finish("not_tested", "صلاحية الكاميرا غير متاحة: " + ex.message)
        } catch (ex: Exception) {
            finish("failed", "تعذر اختبار الكاميرا " + target.id + ": " + ex.message)
        }
    }

    private fun describe(id: String, characteristics: CameraCharacteristics, parentId: String, physicalId: String?, physicalOnly: Boolean): JSONObject {
        val facing = when (characteristics.get(CameraCharacteristics.LENS_FACING)) {
            CameraCharacteristics.LENS_FACING_FRONT -> "front"
            CameraCharacteristics.LENS_FACING_BACK -> "back"
            CameraCharacteristics.LENS_FACING_EXTERNAL -> "external"
            else -> "unknown"
        }
        val focal = JSONArray()
        characteristics.get(CameraCharacteristics.LENS_INFO_AVAILABLE_FOCAL_LENGTHS)?.forEach { focal.put(it) }
        val sensor = characteristics.get(CameraCharacteristics.SENSOR_INFO_PIXEL_ARRAY_SIZE)
        val level = when (characteristics.get(CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL)) {
            CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_LEGACY -> "LEGACY"
            CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_LIMITED -> "LIMITED"
            CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_FULL -> "FULL"
            CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_3 -> "LEVEL_3"
            CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL_EXTERNAL -> "EXTERNAL"
            else -> "UNKNOWN"
        }
        val physical = JSONArray()
        if (Build.VERSION.SDK_INT >= 28) runCatching { characteristics.physicalCameraIds.forEach { physical.put(it) } }
        return JSONObject()
            .put("id", id).put("parentId", parentId)
            .put("physicalId", physicalId ?: JSONObject.NULL)
            .put("facing", facing).put("hardwareLevel", level)
            .put("hasFlash", characteristics.get(CameraCharacteristics.FLASH_INFO_AVAILABLE) == true)
            .put("focalLengthsMm", focal)
            .put("sensorResolution", if (sensor == null) "" else sensor.width.toString() + "x" + sensor.height)
            .put("physicalCameraIds", physical).put("isPhysicalOnly", physicalOnly)
    }

    private fun safeKey(id: String) = id.replace(Regex("[^A-Za-z0-9_-]"), "_")
}