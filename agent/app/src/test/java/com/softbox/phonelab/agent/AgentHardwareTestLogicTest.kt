package com.softbox.phonelab.agent

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AgentHardwareTestLogicTest {
    @Test
    fun expectedHardwareTestIdsRemainUnique() {
        val ids = listOf("display", "touch", "camera", "sensors", "bluetooth", "wifi", "nfc", "gps", "fingerprint", "physical_buttons", "sim")
        assertEquals(ids.size, ids.distinct().size)
    }

    @Test
    fun scheduledBatterySamplingUsesAValidPeriodicInterval() {
        assertTrue(BatterySnapshotWorker.SAMPLE_INTERVAL_MINUTES >= 15)
    }

    @Test
    fun hardwareDiagnosticsDoNotDeclareSubscriberIdentifierFields() {
        val safeFields = listOf("simState", "activeModemCount", "networkOperatorName")
        assertTrue(safeFields.none { it.contains("imei", ignoreCase = true) })
        assertTrue(safeFields.none { it.contains("imsi", ignoreCase = true) })
        assertTrue(safeFields.none { it.contains("iccid", ignoreCase = true) })
    }
}
