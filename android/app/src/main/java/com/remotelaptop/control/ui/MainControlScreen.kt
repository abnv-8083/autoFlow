package com.remotelaptop.control.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.PowerSettingsNew
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Bedtime
import androidx.compose.material.icons.filled.Fingerprint
import androidx.compose.material.icons.filled.Security
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.fragment.app.FragmentActivity
import com.remotelaptop.control.data.CommandResult
import com.remotelaptop.control.data.ConnectionState
import com.remotelaptop.control.data.LaptopStatus
import com.remotelaptop.control.data.SecureStorage
import com.remotelaptop.control.data.WebSocketManager
import com.remotelaptop.control.ui.theme.*
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainControlScreen(
    webSocketManager: WebSocketManager,
    secureStorage: SecureStorage
) {
    val context = LocalContext.current
    val activity = context as? FragmentActivity
    val biometricHelper = remember(activity) { activity?.let { BiometricHelper(it) } }
    val coroutineScope = rememberCoroutineScope()

    val connectionState by webSocketManager.connectionState.collectAsState()
    val laptopStatus by webSocketManager.laptopStatus.collectAsState()

    var showSettings by remember { mutableStateOf(false) }
    var pendingConfirmationCommand by remember { mutableStateOf<String?>(null) }
    val snackbarHostState = remember { SnackbarHostState() }

    fun triggerProtectedCommand(command: String) {
        val bioStatus = biometricHelper?.getBiometricStatus()
        if (bioStatus == BiometricStatus.AVAILABLE) {
            biometricHelper.authenticate(
                title = "Authorize $command",
                subtitle = "Confirm $command using biometric or screen lock PIN",
                onSuccess = {
                    webSocketManager.sendCommand(command)
                },
                onError = { err ->
                    coroutineScope.launch {
                        snackbarHostState.showSnackbar("Authentication: $err")
                    }
                }
            )
        } else {
            // Biometric not enrolled or unavailable; fallback to confirmation dialog
            pendingConfirmationCommand = command
        }
    }

    // Listen for command results and error toasts
    LaunchedEffect(Unit) {
        webSocketManager.lastResult.collectLatest { result ->
            val msg = if (result.success) {
                "${result.command} executed successfully"
            } else {
                "${result.command} failed: ${result.error ?: "Unknown error"}"
            }
            snackbarHostState.showSnackbar(msg)
        }
    }

    LaunchedEffect(Unit) {
        webSocketManager.toastMessages.collectLatest { msg ->
            snackbarHostState.showSnackbar(msg)
        }
    }

    val isOnline = laptopStatus == LaptopStatus.ONLINE

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        "Remote Laptop",
                        fontWeight = FontWeight.Bold,
                        color = TextPrimary
                    )
                },
                actions = {
                    IconButton(onClick = { showSettings = true }) {
                        Icon(
                            imageVector = Icons.Default.Settings,
                            contentDescription = "Settings",
                            tint = TextSecondary
                        )
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = DarkBackground)
            )
        },
        containerColor = DarkBackground
    ) { paddingValues ->

        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .padding(horizontal = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.SpaceBetween
        ) {

            // Header: Laptop Info & Real-Time Status
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 16.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Text(
                    text = secureStorage.laptopName,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = TextPrimary
                )

                Spacer(modifier = Modifier.height(8.dp))

                Row(
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Box(
                        modifier = Modifier
                            .size(10.dp)
                            .clip(CircleShape)
                            .background(if (isOnline) OnlineGreen else OfflineGray)
                    )

                    Spacer(modifier = Modifier.width(8.dp))

                    Text(
                        text = when {
                            connectionState == ConnectionState.CONNECTING -> "Connecting to Relay..."
                            connectionState == ConnectionState.DISCONNECTED -> "Relay Disconnected"
                            isOnline -> "Online"
                            else -> "Offline"
                        },
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Medium,
                        color = if (isOnline) OnlineGreen else TextSecondary
                    )
                }
            }

            // Command Action Buttons
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 24.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                // 1. LOCK (Immediate on click)
                CommandButton(
                    title = "LOCK",
                    subtitle = "Instantly lock workstation",
                    icon = Icons.Default.Lock,
                    iconColor = AccentPrimary,
                    enabled = isOnline,
                    onClick = {
                        webSocketManager.sendCommand("LOCK")
                    }
                )

                // 2. SLEEP (Immediate on click)
                CommandButton(
                    title = "SLEEP",
                    subtitle = "Put laptop into standby",
                    icon = Icons.Default.Bedtime,
                    iconColor = WarningAmber,
                    enabled = isOnline,
                    onClick = {
                        webSocketManager.sendCommand("SLEEP")
                    }
                )

                // 3. RESTART (Requires confirmation / biometric)
                CommandButton(
                    title = "RESTART",
                    subtitle = "Reboot Windows machine",
                    icon = Icons.Default.Refresh,
                    iconColor = WarningAmber,
                    enabled = isOnline,
                    isBiometricProtected = true,
                    onClick = {
                        triggerProtectedCommand("RESTART")
                    }
                )

                // 4. SHUTDOWN (Requires confirmation / biometric)
                CommandButton(
                    title = "SHUTDOWN",
                    subtitle = "Power off the laptop",
                    icon = Icons.Default.PowerSettingsNew,
                    iconColor = DangerRed,
                    enabled = isOnline,
                    isBiometricProtected = true,
                    onClick = {
                        triggerProtectedCommand("SHUTDOWN")
                    }
                )
            }

            // Bottom Spacer
            Spacer(modifier = Modifier.height(16.dp))
        }
    }

    // Confirmation Dialog for Destructive Actions (Fallback when Biometric is not enrolled)
    if (pendingConfirmationCommand != null) {
        val command = pendingConfirmationCommand!!
        AlertDialog(
            onDismissRequest = { pendingConfirmationCommand = null },
            title = {
                Text(
                    text = "Confirm $command",
                    color = TextPrimary,
                    fontWeight = FontWeight.Bold
                )
            },
            text = {
                Text(
                    text = "Biometric lock is not set up on this device. Are you sure you want to $command your laptop remotely?",
                    color = TextSecondary
                )
            },
            confirmButton = {
                Button(
                    onClick = {
                        webSocketManager.sendCommand(command)
                        pendingConfirmationCommand = null
                    },
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (command == "SHUTDOWN") DangerRed else WarningAmber
                    )
                ) {
                    Text("Confirm $command")
                }
            },
            dismissButton = {
                TextButton(onClick = { pendingConfirmationCommand = null }) {
                    Text("Cancel", color = TextSecondary)
                }
            },
            containerColor = DarkSurface
        )
    }

    // Settings Modal
    if (showSettings) {
        SettingsDialog(
            secureStorage = secureStorage,
            onDismiss = { showSettings = false },
            onReconnect = { webSocketManager.connect() },
            onUnpair = { webSocketManager.unpair() }
        )
    }
}

@Composable
fun CommandButton(
    title: String,
    subtitle: String,
    icon: ImageVector,
    iconColor: Color,
    enabled: Boolean,
    isBiometricProtected: Boolean = false,
    onClick: () -> Unit
) {
    Surface(
        shape = RoundedCornerShape(16.dp),
        color = DarkCard,
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .border(
                width = 1.dp,
                color = if (enabled) DarkBorder else DarkBorder.copy(alpha = 0.5f),
                shape = RoundedCornerShape(16.dp)
            )
            .clickable(enabled = enabled, onClick = onClick)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 18.dp, horizontal = 20.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(if (enabled) iconColor.copy(alpha = 0.15f) else DarkBorder.copy(alpha = 0.3f)),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = icon,
                    contentDescription = title,
                    tint = if (enabled) iconColor else OfflineGray,
                    modifier = Modifier.size(24.dp)
                )
            }

            Spacer(modifier = Modifier.width(16.dp))

            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    fontSize = 17.sp,
                    fontWeight = FontWeight.Bold,
                    color = if (enabled) TextPrimary else OfflineGray
                )
                Text(
                    text = subtitle,
                    fontSize = 12.sp,
                    color = if (enabled) TextSecondary else OfflineGray.copy(alpha = 0.6f)
                )
            }

            if (isBiometricProtected) {
                Surface(
                    shape = RoundedCornerShape(8.dp),
                    color = DarkBackground,
                    border = androidx.compose.foundation.BorderStroke(width = 1.dp, color = AccentSecondary.copy(alpha = 0.3f))
                ) {
                    Row(
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Icon(
                            imageVector = Icons.Default.Fingerprint,
                            contentDescription = "Protected",
                            tint = if (enabled) AccentSecondary else OfflineGray,
                            modifier = Modifier.size(14.dp)
                        )
                        Spacer(modifier = Modifier.width(4.dp))
                        Text(
                            text = "GUARDED",
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Bold,
                            color = if (enabled) AccentSecondary else OfflineGray
                        )
                    }
                }
            }
        }
    }
}
