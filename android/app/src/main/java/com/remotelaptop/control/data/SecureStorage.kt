package com.remotelaptop.control.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import java.util.UUID

class SecureStorage(context: Context) {

    private val prefs: SharedPreferences = try {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()

        EncryptedSharedPreferences.create(
            context,
            "remote_laptop_secure_prefs",
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    } catch (e: Exception) {
        // Fallback to standard SharedPreferences if Keystore is unavailable
        context.getSharedPreferences("remote_laptop_fallback_prefs", Context.MODE_PRIVATE)
    }

    // Fixed default relay server URL (Users cannot change this link)
    val serverUrl: String
        get() = DEFAULT_SERVER_URL

    var deviceId: String
        get() {
            var id = prefs.getString(KEY_DEVICE_ID, null)
            if (id == null) {
                id = "android-" + UUID.randomUUID().toString().take(8)
                prefs.edit().putString(KEY_DEVICE_ID, id).apply()
            }
            return id
        }
        set(value) = prefs.edit().putString(KEY_DEVICE_ID, value).apply()

    var authToken: String
        get() = prefs.getString(KEY_AUTH_TOKEN, "dev-secret-token-123") ?: "dev-secret-token-123"
        set(value) = prefs.edit().putString(KEY_AUTH_TOKEN, value.trim()).apply()

    var laptopName: String
        get() = prefs.getString(KEY_LAPTOP_NAME, "My Windows Laptop") ?: "My Windows Laptop"
        set(value) = prefs.edit().putString(KEY_LAPTOP_NAME, value.trim()).apply()

    var isPaired: Boolean
        get() = prefs.getBoolean(KEY_IS_PAIRED, prefs.getString(KEY_AUTH_TOKEN, null) != null)
        set(value) = prefs.edit().putBoolean(KEY_IS_PAIRED, value).apply()

    fun clearAll() {
        prefs.edit().clear().apply()
    }

    companion object {
        // Change this constant when your cloud server (e.g. Render/Railway) is deployed:
        const val DEFAULT_SERVER_URL = "wss://remote-laptop.onrender.com"

        private const val KEY_SERVER_URL = "server_url"
        private const val KEY_DEVICE_ID = "device_id"
        private const val KEY_AUTH_TOKEN = "auth_token"
        private const val KEY_LAPTOP_NAME = "laptop_name"
        private const val KEY_IS_PAIRED = "is_paired"
    }
}
