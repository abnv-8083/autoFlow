package com.remotelaptop.control

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import com.remotelaptop.control.data.SecureStorage
import com.remotelaptop.control.data.WebSocketManager
import com.remotelaptop.control.ui.MainControlScreen
import com.remotelaptop.control.ui.theme.RemoteLaptopTheme

import androidx.fragment.app.FragmentActivity
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import com.remotelaptop.control.ui.PairingScreen

class MainActivity : FragmentActivity() {

    private lateinit var secureStorage: SecureStorage
    private lateinit var webSocketManager: WebSocketManager

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        secureStorage = SecureStorage(this)
        webSocketManager = WebSocketManager(secureStorage)

        setContent {
            RemoteLaptopTheme {
                val isPaired by webSocketManager.isPairedFlow.collectAsState()

                if (isPaired) {
                    MainControlScreen(
                        webSocketManager = webSocketManager,
                        secureStorage = secureStorage
                    )
                } else {
                    PairingScreen(
                        webSocketManager = webSocketManager,
                        secureStorage = secureStorage,
                        onPairingSuccess = {
                            webSocketManager.connect()
                        }
                    )
                }
            }
        }
    }

    override fun onStart() {
        super.onStart()
        // Automatically connect to WebSocket relay server when app opens
        webSocketManager.connect()
    }

    override fun onDestroy() {
        super.onDestroy()
        webSocketManager.disconnect()
    }
}
