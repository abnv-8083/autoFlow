package com.remotelaptop.control.data

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.concurrent.TimeUnit

class WebSocketManager(private val secureStorage: SecureStorage) {

    private val client = OkHttpClient.Builder()
        .pingInterval(25, TimeUnit.SECONDS)
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS) // Indefinite for WebSocket
        .build()

    private var webSocket: WebSocket? = null
    private var reconnectJob: Job? = null
    private val scope = CoroutineScope(Dispatchers.IO)
    private var isIntentionalClose = false

    private val _connectionState = MutableStateFlow(ConnectionState.DISCONNECTED)
    val connectionState = _connectionState.asStateFlow()

    private val _laptopStatus = MutableStateFlow(LaptopStatus.OFFLINE)
    val laptopStatus = _laptopStatus.asStateFlow()

    private val _lastResult = MutableSharedFlow<CommandResult>(extraBufferCapacity = 1)
    val lastResult = _lastResult.asSharedFlow()

    private val _toastMessages = MutableSharedFlow<String>(extraBufferCapacity = 1)
    val toastMessages = _toastMessages.asSharedFlow()

    private val _isPairedFlow = MutableStateFlow(secureStorage.isPaired)
    val isPairedFlow = _isPairedFlow.asStateFlow()

    private val _pairingLoading = MutableStateFlow(false)
    val pairingLoading = _pairingLoading.asStateFlow()

    private var pendingMessageOnConnect: String? = null

    fun connect() {
        isIntentionalClose = false
        reconnectJob?.cancel()

        if (_connectionState.value == ConnectionState.CONNECTED) return

        _connectionState.value = ConnectionState.CONNECTING

        val url = secureStorage.serverUrl
        val request = try {
            Request.Builder().url(url).build()
        } catch (e: Exception) {
            _connectionState.value = ConnectionState.DISCONNECTED
            _toastMessages.tryEmit("Invalid server URL: ${e.message}")
            return
        }

        webSocket?.cancel()
        webSocket = client.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(ws: WebSocket, response: Response) {
                _connectionState.value = ConnectionState.CONNECTED

                // Send pending message if any
                if (pendingMessageOnConnect != null) {
                    ws.send(pendingMessageOnConnect!!)
                    pendingMessageOnConnect = null
                } else if (secureStorage.authToken.isNotBlank()) {
                    // Send Auth message immediately if token exists
                    val authMsg = RemoteProtocol.createAuthMessage(
                        deviceId = secureStorage.deviceId,
                        token = secureStorage.authToken
                    )
                    ws.send(authMsg)
                }
            }

            override fun onMessage(ws: WebSocket, text: String) {
                try {
                    val json = JSONObject(text)
                    when (json.optString("type")) {
                        "auth_result" -> {
                            val success = json.optBoolean("success", false)
                            val msg = json.optString("message", "")
                            if (success) {
                                _toastMessages.tryEmit("Phone connected to relay")
                            } else {
                                _toastMessages.tryEmit("Authentication failed: $msg")
                            }
                        }
                        "pairing_success" -> {
                            _pairingLoading.value = false
                            val result = RemoteProtocol.parsePairingSuccess(text)
                            if (result.token != null) {
                                secureStorage.authToken = result.token
                                if (result.laptopId != null) {
                                    secureStorage.laptopName = result.laptopId
                                }
                                secureStorage.isPaired = true
                                _isPairedFlow.value = true
                                _toastMessages.tryEmit("Paired with ${result.laptopId ?: "laptop"} successfully!")
                            }
                        }
                        "pairing_failed" -> {
                            _pairingLoading.value = false
                            val result = RemoteProtocol.parsePairingFailed(text)
                            _toastMessages.tryEmit(result.error ?: "Pairing failed")
                        }
                        "status" -> {
                            val (status, laptopId) = RemoteProtocol.parseStatus(text)
                            _laptopStatus.value = status
                            if (laptopId != null) {
                                secureStorage.laptopName = laptopId
                            }
                        }
                        "command_result" -> {
                            val result = RemoteProtocol.parseCommandResult(text)
                            _lastResult.tryEmit(result)
                        }
                        "error" -> {
                            _pairingLoading.value = false
                            val err = json.optString("message", "Unknown error")
                            _toastMessages.tryEmit("Server: $err")
                        }
                    }
                } catch (e: Exception) {
                    // Silently ignore or log malformed messages
                }
            }

            override fun onClosing(ws: WebSocket, code: Int, reason: String) {
                ws.close(1000, null)
            }

            override fun onClosed(ws: WebSocket, code: Int, reason: String) {
                handleDisconnect()
            }

            override fun onFailure(ws: WebSocket, t: Throwable, response: Response?) {
                handleDisconnect()
            }
        })
    }

    private fun handleDisconnect() {
        _connectionState.value = ConnectionState.DISCONNECTED
        _laptopStatus.value = LaptopStatus.OFFLINE

        if (!isIntentionalClose) {
            scheduleReconnect()
        }
    }

    private fun scheduleReconnect() {
        reconnectJob?.cancel()
        reconnectJob = scope.launch {
            delay(4000)
            if (!isIntentionalClose && _connectionState.value == ConnectionState.DISCONNECTED) {
                connect()
            }
        }
    }

    fun disconnect() {
        isIntentionalClose = true
        reconnectJob?.cancel()
        webSocket?.close(1000, "User disconnected")
        webSocket = null
        _connectionState.value = ConnectionState.DISCONNECTED
        _laptopStatus.value = LaptopStatus.OFFLINE
    }

    fun sendCommand(command: String): Boolean {
        val ws = webSocket
        if (ws == null || _connectionState.value != ConnectionState.CONNECTED) {
            _toastMessages.tryEmit("Not connected to server")
            return false
        }

        if (_laptopStatus.value != LaptopStatus.ONLINE) {
            _toastMessages.tryEmit("Cannot send: Laptop is offline")
            return false
        }

        val (_, json) = RemoteProtocol.createCommandMessage(command)
        return ws.send(json)
    }

    fun pairWithCode(code: String) {
        val cleanCode = code.trim()
        if (cleanCode.length != 6) {
            _toastMessages.tryEmit("Pairing PIN must be 6 digits")
            return
        }

        _pairingLoading.value = true
        val pairMsg = RemoteProtocol.createPairWithCodeMessage(
            phoneDeviceId = secureStorage.deviceId,
            code = cleanCode
        )

        val ws = webSocket
        if (ws != null && _connectionState.value == ConnectionState.CONNECTED) {
            ws.send(pairMsg)
        } else {
            pendingMessageOnConnect = pairMsg
            connect()
        }
    }

    fun unpair() {
        secureStorage.isPaired = false
        secureStorage.authToken = ""
        _isPairedFlow.value = false
        disconnect()
        _toastMessages.tryEmit("Unpaired from laptop")
    }
}
