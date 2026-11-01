package com.example.moodify_application

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.os.Build
import io.flutter.plugin.common.BinaryMessenger
import io.flutter.plugin.common.MethodChannel

// Displays already-received FCM reminders; it does not schedule or send pushes.
class SystemNotificationBridge(
    private val activity: MainActivity,
    messenger: BinaryMessenger
) {
    companion object {
        const val CHANNEL_ID = "moodify_reminders_v2"
        private const val LEGACY_CHANNEL = "moodify_reminders"
        private const val METHOD_CHANNEL = "moodify/system_notifications"
        private const val OPEN_ACTION = "com.example.moodify_application.OPEN_REMINDER"
        private const val SCREEN_EXTRA = "moodify_reminder_screen"
        private const val TAG = "moodify_reminder"
    }

    private val manager = activity.getSystemService(NotificationManager::class.java)
    private val channel = MethodChannel(messenger, METHOD_CHANNEL)
    private var ready = false
    private var pendingScreen: String? = null

    init {
        ensureChannel()
        channel.setMethodCallHandler { call, result ->
            try {
                when (call.method) {
                    "initialize" -> {
                        ready = true
                        val screen = pendingScreen
                        pendingScreen = null
                        result.success(screen)
                    }
                    "show" -> result.success(show(
                        call.argument<String>("title") ?: "Moodify",
                        call.argument<String>("body") ?: "",
                        call.argument<String>("screen") ?: "discover",
                        call.argument<String>("messageId")
                    ))
                    "cancelAll" -> {
                        cancelReminders()
                        result.success(null)
                    }
                    else -> result.notImplemented()
                }
            } catch (_: SecurityException) {
                result.success(false)
            } catch (_: Exception) {
                result.error("notification_unavailable", "Unable to display reminder.", null)
            }
        }
    }

    private fun ensureChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        if (manager.getNotificationChannel(CHANNEL_ID) != null) return
        val old = manager.getNotificationChannel(LEGACY_CHANNEL)
        // Preserve user choices when moving from the default-priority old channel.
        val importance = when {
            old?.importance == NotificationManager.IMPORTANCE_NONE -> NotificationManager.IMPORTANCE_NONE
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && old?.hasUserSetImportance() == true -> old.importance
            old != null && old.importance < NotificationManager.IMPORTANCE_DEFAULT -> old.importance
            else -> NotificationManager.IMPORTANCE_HIGH
        }
        val reminderChannel = NotificationChannel(CHANNEL_ID, "Lời nhắc Moodify", importance)
        reminderChannel.description = "Nhắc ghi nhận tâm trạng và nhìn lại tuần"
        if (old != null) {
            reminderChannel.setSound(old.sound, old.audioAttributes)
            reminderChannel.enableVibration(old.shouldVibrate())
            reminderChannel.vibrationPattern = old.vibrationPattern
            reminderChannel.setShowBadge(old.canShowBadge())
        }
        manager.createNotificationChannel(reminderChannel)
    }

    @Suppress("DEPRECATION")
    private fun show(title: String, body: String, screen: String, messageId: String?): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N && !manager.areNotificationsEnabled()) return false
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) return false
        ensureChannel()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O &&
            manager.getNotificationChannel(CHANNEL_ID)?.importance == NotificationManager.IMPORTANCE_NONE
        ) return false
        val destination = if (screen == "statistics") "statistics" else "discover"
        val id = (messageId?.takeIf { it.isNotBlank() }?.hashCode()
            ?: System.currentTimeMillis().toInt()) and Int.MAX_VALUE
        val launch = Intent(activity, MainActivity::class.java).apply {
            action = OPEN_ACTION
            flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            putExtra(SCREEN_EXTRA, destination)
        }
        val tap = PendingIntent.getActivity(
            activity, id, launch, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(activity, CHANNEL_ID)
        } else {
            Notification.Builder(activity).setPriority(Notification.PRIORITY_HIGH)
        }
        val notification = builder
            .setSmallIcon(R.drawable.moodify_icon_monochrome)
            .setColor(Color.rgb(139, 92, 246))
            .setContentTitle(title.take(120))
            .setContentText(body.take(2000))
            .setStyle(Notification.BigTextStyle().bigText(body.take(2000)))
            .setCategory(Notification.CATEGORY_REMINDER)
            .setVisibility(Notification.VISIBILITY_PRIVATE)
            .setContentIntent(tap)
            .setAutoCancel(true)
            .setOnlyAlertOnce(true)
            .build()
        manager.notify(TAG, id, notification)
        return true
    }

    fun handleIntent(intent: Intent?) {
        if (intent?.action != OPEN_ACTION) return
        val screen = intent.getStringExtra(SCREEN_EXTRA)
        if (screen != "discover" && screen != "statistics") return
        if (ready) channel.invokeMethod("opened", screen) else pendingScreen = screen
    }

    private fun cancelReminders() {
        for (entry in manager.activeNotifications) {
            val ours = entry.tag == TAG || (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O &&
                entry.notification.channelId in setOf(CHANNEL_ID, LEGACY_CHANNEL))
            if (ours) manager.cancel(entry.tag, entry.id)
        }
    }

    fun dispose() {
        ready = false
        channel.setMethodCallHandler(null)
    }
}
