package com.softbox.phonelab.agent

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.view.MotionEvent
import android.view.View

class TouchGridTestView(context: Context, private val onComplete: (Int, Int) -> Unit) : View(context) {
    private val columns = 5
    private val rows = 8
    private val touched = BooleanArray(columns * rows)
    private val linePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        strokeWidth = 2f
        style = Paint.Style.STROKE
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = 28f
        textAlign = Paint.Align.CENTER
    }
    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private var completed = false

    init { setBackgroundColor(Color.rgb(8, 19, 33)); isFocusable = true }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cellWidth = width.toFloat() / columns
        val cellHeight = height.toFloat() / rows
        for (row in 0 until rows) {
            for (column in 0 until columns) {
                val index = row * columns + column
                val left = column * cellWidth
                val top = row * cellHeight
                fillPaint.color = if (touched[index]) Color.rgb(17, 138, 112) else Color.rgb(21, 43, 65)
                fillPaint.style = Paint.Style.FILL
                canvas.drawRect(left + 4, top + 4, left + cellWidth - 4, top + cellHeight - 4, fillPaint)
                canvas.drawRect(left + 4, top + 4, left + cellWidth - 4, top + cellHeight - 4, linePaint)
                canvas.drawText(if (touched[index]) "✓" else "${index + 1}", left + cellWidth / 2, top + cellHeight / 2 + 10, textPaint)
            }
        }
        val count = touched.count { it }
        textPaint.textSize = 24f
        canvas.drawText("المناطق الملموسة: ${count} / ${touched.size}", width / 2f, height - 12f, textPaint)
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (event.action == MotionEvent.ACTION_DOWN || event.action == MotionEvent.ACTION_MOVE) {
            val column = (event.x / (width.toFloat() / columns)).toInt().coerceIn(0, columns - 1)
            val row = (event.y / (height.toFloat() / rows)).toInt().coerceIn(0, rows - 1)
            touched[row * columns + column] = true
            invalidate()
            val count = touched.count { it }
            if (!completed && count == touched.size) {
                completed = true
                post { onComplete(count, touched.size) }
            }
            return true
        }
        return true
    }
}
