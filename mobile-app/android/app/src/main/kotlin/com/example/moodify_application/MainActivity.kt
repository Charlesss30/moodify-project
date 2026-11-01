package com.example.moodify_application

import android.content.Intent
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine

class MainActivity : FlutterActivity() {
    private var reminders: SystemNotificationBridge? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        reminders = SystemNotificationBridge(this, flutterEngine.dartExecutor.binaryMessenger)
        reminders?.handleIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        reminders?.handleIntent(intent)
    }

    override fun cleanUpFlutterEngine(flutterEngine: FlutterEngine) {
        reminders?.dispose()
        reminders = null
        super.cleanUpFlutterEngine(flutterEngine)
    }
}
