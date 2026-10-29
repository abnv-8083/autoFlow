package com.remotelaptop.control.data

import org.json.JSONObject
import java.util.UUID

enum class LaptopStatus {
    OFFLINE,
    ONLINE
}

enum class ConnectionState {
    DISCONNECTED,
    CONNECTING,
    CONNECTED
}

data class CommandResult(
    val requestId: String,
    val command: String,
    val success: Boolean,
    val error: String? = null
)

data class PairingResult(
    val success: Boolean,
    val token: String? = null,
    val laptopId: String? = null,
    val error: String? = null
)

object RemoteProtocol {

    fun createAuthMessage(deviceId: String, token: String): String {
        return JSONObject().apply {
            put("type", "auth")
            put("deviceId", deviceId)
            put("deviceType", "phone")
            put("token", token)
        }.toString()
    }

    fun createPairWithCodeMessage(phoneDeviceId: String, code: String): String {
        return JSONObject().apply {
            put("type", "pair_with_code")
            put("phoneDeviceId", phoneDeviceId)
            put("code", code)
        }.toString()
    }

    fun parsePairingSuccess(jsonString: String): PairingResult {
        val json = JSONObject(jsonString)
        val token = if (json.has("token")) json.getString("token") else null
        val laptopId = if (json.has("laptopId")) json.getString("laptopId") else null
        return PairingResult(
            success = true,
            token = token,
            laptopId = laptopId
        )
    }

    fun parsePairingFailed(jsonString: String): PairingResult {
        val json = JSONObject(jsonString)
        return PairingResult(
            success = false,
            error = json.optString("message", "Pairing failed")
        )
    }

    fun createCommandMessage(command: String): Pair<String, String> {
        val requestId = "req-" + UUID.randomUUID().toString().take(8)
        val json = JSONObject().apply {
            put("type", "command")
            put("requestId", requestId)
            put("command", command)
        }.toString()
        return Pair(requestId, json)
    }

    fun parseStatus(jsonString: String): Pair<LaptopStatus, String?> {
        val json = JSONObject(jsonString)
        val statusStr = json.optString("status", "offline")
        val laptopId = if (json.has("laptopId")) json.getString("laptopId") else null
        val status = if (statusStr.equals("online", ignoreCase = true)) {
            LaptopStatus.ONLINE
        } else {
            LaptopStatus.OFFLINE
        }
        return Pair(status, laptopId)
    }

    fun parseCommandResult(jsonString: String): CommandResult {
        val json = JSONObject(jsonString)
        return CommandResult(
            requestId = json.optString("requestId", ""),
            command = json.optString("command", ""),
            success = json.optBoolean("success", false),
            error = if (json.has("error")) json.getString("error") else null
        )
    }
}
