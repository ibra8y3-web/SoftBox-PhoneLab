package com.softbox.phonelab.agent

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.core.app.ActivityScenario
import androidx.test.espresso.Espresso.onView
import androidx.test.espresso.assertion.ViewAssertions.matches
import androidx.test.espresso.action.ViewActions.click
import androidx.test.espresso.action.ViewActions.scrollTo
import androidx.test.espresso.matcher.ViewMatchers.isDisplayed
import androidx.test.espresso.matcher.ViewMatchers.withId
import androidx.test.espresso.matcher.ViewMatchers.withText
import org.hamcrest.Matchers.containsString
import org.junit.After
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class PhoneLabAgentUiTest {
    private lateinit var scenario: ActivityScenario<MainActivity>

    @Before
    fun launch() {
        scenario = ActivityScenario.launch(MainActivity::class.java)
        onView(withText("السماح لهذا الاتصال")).perform(click())
    }

    @After
    fun close() {
        scenario.close()
    }

    @Test
    fun allHardwareTestControlsArePresent() {
        listOf(
            R.id.testDisplay, R.id.testTouch, R.id.testCamera, R.id.testSensors,
            R.id.testBluetooth, R.id.testWifi, R.id.testNfc, R.id.testGps,
            R.id.testFingerprint, R.id.testButtons, R.id.testSim,
            R.id.startBatteryCapacity, R.id.finishBatteryCapacity
        ).forEach { id -> onView(withId(id)).perform(scrollTo()).check(matches(isDisplayed())) }
    }

    @Test
    fun inventoryTestCanBeStartedFromTheAgentUi() {
        onView(withId(R.id.testSensors)).perform(click())
        Thread.sleep(3200L)
        onView(withId(R.id.results)).check(matches(withText(containsString("sensors"))))
    }
}
