/**
 * Laptop Device Dashboard & Security PIN Gateway
 * Remote Laptop Control System
 */

document.addEventListener('DOMContentLoaded', () => {
  initDashboardController();
});

function initDashboardController() {
  // DOM Elements - Screens
  const pinScreen = document.getElementById('pin-screen');
  const dashboardScreen = document.getElementById('dashboard-screen');
  const pinCardBox = document.getElementById('pin-card-box');
  const pinTitle = document.getElementById('pin-title');
  const pinSubtitle = document.getElementById('pin-subtitle');
  const pinFeedback = document.getElementById('pin-feedback');
  const pinBoxes = document.querySelectorAll('.pin-box');
  const pinHiddenInput = document.getElementById('pin-hidden-input');
  const keypadButtons = document.querySelectorAll('.key-btn');
  const btnResetPin = document.getElementById('btn-reset-pin');

  // DOM Elements - Navigation & Relay Status
  const relayPill = document.getElementById('dash-relay-pill');
  const relayDot = document.getElementById('dash-relay-dot');
  const relayText = document.getElementById('dash-relay-text');
  const telemetryAgent = document.getElementById('telemetry-agent');
  const telemetryRelay = document.getElementById('telemetry-relay');
  const telemetryLatency = document.getElementById('telemetry-latency');

  // DOM Elements - Dashboard Header Controls
  const btnLockDash = document.getElementById('btn-lock-dash');
  const btnChangePin = document.getElementById('btn-change-pin');

  // DOM Elements - Pairing Center
  const pairingCodeVal = document.getElementById('pairing-code-val');
  const pairingCountdown = document.getElementById('pairing-countdown');
  const pairingTimerBar = document.getElementById('pairing-timer-bar');
  const btnGeneratePin = document.getElementById('btn-generate-pin');
  const btnCopyPairing = document.getElementById('btn-copy-pairing');

  // DOM Elements - Power Action Cards
  const actionLock = document.getElementById('action-lock-laptop');
  const actionSleep = document.getElementById('action-sleep-laptop');
  const actionRestart = document.getElementById('action-restart-laptop');
  const actionShutdown = document.getElementById('action-shutdown-laptop');

  // DOM Elements - Audit Log
  const auditList = document.getElementById('audit-list');
  const btnClearLogs = document.getElementById('btn-clear-logs');

  // DOM Elements - Safety Modal
  const safetyModal = document.getElementById('safety-modal');
  const modalTitle = document.getElementById('modal-title');
  const modalDesc = document.getElementById('modal-desc');
  const modalBtnCancel = document.getElementById('modal-btn-cancel');
  const modalBtnConfirm = document.getElementById('modal-btn-confirm');
  let pendingModalAction = null;

  // State Management
  const STORAGE_KEY_PIN_HASH = 'rlc_laptop_master_pin';
  const SESSION_KEY_UNLOCKED = 'rlc_laptop_session_unlocked';

  let currentInput = '';
  let tempFirstPin = '';
  let pinTimerInterval = null;
  let remainingSeconds = 300;
  let ws = null;
  let wsLatency = 8;

  // Modes: 'SETUP_FIRST', 'SETUP_CONFIRM', 'UNLOCK'
  let currentMode = 'UNLOCK';

  // 1. Initialize PIN Gateway State
  checkPinState();

  function checkPinState() {
    const storedHash = localStorage.getItem(STORAGE_KEY_PIN_HASH);
    const isUnlocked = sessionStorage.getItem(SESSION_KEY_UNLOCKED) === 'true';

    if (!storedHash) {
      currentMode = 'SETUP_FIRST';
      pinTitle.textContent = 'Setup Security PIN';
      pinSubtitle.textContent = 'Create a 6-digit master PIN to secure your workstation controls on this laptop.';
      btnResetPin.style.display = 'none';
      showScreen('pin');
    } else if (isUnlocked) {
      showScreen('dashboard');
      connectRelayWebSocket();
    } else {
      currentMode = 'UNLOCK';
      pinTitle.textContent = 'Enter Security PIN';
      pinSubtitle.textContent = 'Enter your 6-digit master PIN to unlock laptop controls.';
      btnResetPin.style.display = 'inline-block';
      showScreen('pin');
    }
    clearPinDisplay();
  }

  function showScreen(screen) {
    if (screen === 'dashboard') {
      pinScreen.style.display = 'none';
      dashboardScreen.style.display = 'block';
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } else {
      dashboardScreen.style.display = 'none';
      pinScreen.style.display = 'flex';
      if (pinHiddenInput) {
        setTimeout(() => pinHiddenInput.focus(), 150);
      }
    }
  }

  // 2. PIN Input & Visual Feedback
  function updatePinBoxes() {
    pinBoxes.forEach((box, idx) => {
      if (idx < currentInput.length) {
        box.classList.add('filled');
        box.textContent = '●';
      } else {
        box.classList.remove('filled');
        box.textContent = '';
      }
    });
  }

  function clearPinDisplay() {
    currentInput = '';
    updatePinBoxes();
  }

  async function handleDigitInput(digit) {
    if (currentInput.length >= 6) return;

    currentInput += digit;
    updatePinBoxes();
    hideFeedback();

    if (currentInput.length === 6) {
      setTimeout(() => processPinSubmission(), 120);
    }
  }

  function handleBackspace() {
    if (currentInput.length > 0) {
      currentInput = currentInput.slice(0, -1);
      updatePinBoxes();
      hideFeedback();
    }
  }

  function handleClear() {
    clearPinDisplay();
    hideFeedback();
  }

  // 3. Process 6-digit PIN Entry
  async function processPinSubmission() {
    const enteredPin = currentInput;

    if (currentMode === 'SETUP_FIRST') {
      tempFirstPin = enteredPin;
      currentMode = 'SETUP_CONFIRM';
      pinTitle.textContent = 'Confirm Security PIN';
      pinSubtitle.textContent = 'Re-enter your 6-digit PIN to confirm setup.';
      clearPinDisplay();
      showFeedback('PIN captured. Please re-enter to confirm.', 'info');
    } else if (currentMode === 'SETUP_CONFIRM') {
      if (enteredPin === tempFirstPin) {
        const hash = await hashPin(enteredPin);
        localStorage.setItem(STORAGE_KEY_PIN_HASH, hash);
        sessionStorage.setItem(SESSION_KEY_UNLOCKED, 'true');

        showFeedback('Master PIN Created Successfully! Unlocking...', 'success');
        addAuditLog('PIN SETUP', 'New Master Security PIN successfully configured on laptop', 'success');

        setTimeout(() => {
          showScreen('dashboard');
          connectRelayWebSocket();
          showToast('Security PIN successfully configured. Dashboard unlocked!', 'success');
        }, 600);
      } else {
        triggerShake();
        showFeedback('PINs do not match. Please setup your PIN again.', 'danger');
        currentMode = 'SETUP_FIRST';
        tempFirstPin = '';
        pinTitle.textContent = 'Setup Security PIN';
        pinSubtitle.textContent = 'Create a 6-digit master PIN to secure your workstation controls.';
        clearPinDisplay();
      }
    } else if (currentMode === 'UNLOCK') {
      const storedHash = localStorage.getItem(STORAGE_KEY_PIN_HASH);
      const enteredHash = await hashPin(enteredPin);

      if (enteredHash === storedHash) {
        sessionStorage.setItem(SESSION_KEY_UNLOCKED, 'true');
        showFeedback('PIN Verified! Unlocking workstation dashboard...', 'success');
        addAuditLog('PIN AUTH', 'Dashboard unlocked via master PIN authentication', 'success');

        setTimeout(() => {
          showScreen('dashboard');
          connectRelayWebSocket();
          showToast('Welcome back! Workstation dashboard unlocked.', 'success');
        }, 400);
      } else {
        triggerShake();
        showFeedback('Incorrect PIN. Please try again.', 'danger');
        addAuditLog('PIN DENIED', 'Failed PIN authentication attempt', 'danger');
        clearPinDisplay();
      }
    }
  }

  function triggerShake() {
    if (pinCardBox) {
      pinCardBox.classList.remove('shake-animation');
      void pinCardBox.offsetWidth; // force DOM reflow
      pinCardBox.classList.add('shake-animation');
      setTimeout(() => pinCardBox.classList.remove('shake-animation'), 500);
    }
  }

  function showFeedback(msg, type = 'info') {
    if (!pinFeedback) return;
    pinFeedback.textContent = msg;
    pinFeedback.style.display = 'block';
    if (type === 'danger') {
      pinFeedback.style.background = 'rgba(239, 68, 68, 0.15)';
      pinFeedback.style.border = '1px solid rgba(239, 68, 68, 0.35)';
      pinFeedback.style.color = '#f87171';
    } else if (type === 'success') {
      pinFeedback.style.background = 'rgba(16, 185, 129, 0.15)';
      pinFeedback.style.border = '1px solid rgba(16, 185, 129, 0.35)';
      pinFeedback.style.color = '#34d399';
    } else {
      pinFeedback.style.background = 'rgba(59, 130, 246, 0.15)';
      pinFeedback.style.border = '1px solid rgba(59, 130, 246, 0.35)';
      pinFeedback.style.color = '#60a5fa';
    }
  }

  function hideFeedback() {
    if (pinFeedback) pinFeedback.style.display = 'none';
  }

  // SHA-256 Web Crypto Implementation
  async function hashPin(pin) {
    const encoder = new TextEncoder();
    const data = encoder.encode('rlc-salt-' + pin);
    const hashBuffer = await crypto.subtle.digest('SHA-256', data);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
  }

  // 4. Input Listeners: Keypad & Keyboard
  keypadButtons.forEach(btn => {
    btn.addEventListener('click', (e) => {
      e.preventDefault();
      const key = btn.getAttribute('data-key');
      if (key === 'clear') {
        handleClear();
      } else if (key === 'backspace') {
        handleBackspace();
      } else if (key && key.match(/^[0-9]$/)) {
        handleDigitInput(key);
      }
    });
  });

  // Physical Keyboard Listener
  document.addEventListener('keydown', (e) => {
    if (pinScreen.style.display === 'none') return;

    if (e.key >= '0' && e.key <= '9') {
      handleDigitInput(e.key);
    } else if (e.key === 'Backspace') {
      handleBackspace();
    } else if (e.key === 'Escape' || e.key.toLowerCase() === 'c') {
      handleClear();
    }
  });

  // Reset / Change PIN button in Gateway
  if (btnResetPin) {
    btnResetPin.addEventListener('click', () => {
      if (confirm('Reset Master PIN? You will be prompted to create a new 6-digit PIN.')) {
        localStorage.removeItem(STORAGE_KEY_PIN_HASH);
        sessionStorage.removeItem(SESSION_KEY_UNLOCKED);
        checkPinState();
        showFeedback('Existing PIN reset. Enter a new 6-digit master PIN.', 'info');
      }
    });
  }

  // 5. Dashboard Lock & Change PIN Controls
  if (btnLockDash) {
    btnLockDash.addEventListener('click', () => {
      sessionStorage.removeItem(SESSION_KEY_UNLOCKED);
      addAuditLog('LOCK', 'Dashboard locked by user request', 'info');
      showToast('Dashboard locked. Enter PIN to access.', 'info');
      checkPinState();
    });
  }

  if (btnChangePin) {
    btnChangePin.addEventListener('click', () => {
      if (confirm('Change your Master PIN? This will lock the dashboard and start the PIN setup wizard.')) {
        localStorage.removeItem(STORAGE_KEY_PIN_HASH);
        sessionStorage.removeItem(SESSION_KEY_UNLOCKED);
        addAuditLog('PIN RESET', 'User initiated Master PIN reconfiguration', 'warning');
        checkPinState();
      }
    });
  }

  // 6. WebSocket Live Connection
  function connectRelayWebSocket() {
    if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
      return;
    }

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${window.location.host}`;

    updateRelayStatus('connecting', 'Connecting to Relay...');

    try {
      ws = new WebSocket(wsUrl);

      ws.onopen = () => {
        updateRelayStatus('online', 'Relay Online (ws://8080)');
        if (telemetryRelay) telemetryRelay.textContent = 'Connected';
        if (telemetryAgent) telemetryAgent.textContent = 'Active';
        addAuditLog('RELAY', 'Secure WebSocket connection established with relay server', 'success');

        // Request an initial dynamic pairing PIN for convenience
        requestNewPairingPin();
      };

      ws.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          handleWebSocketMessage(data);
        } catch (err) {
          console.error('[Dashboard WS] Malformed JSON:', err);
        }
      };

      ws.onclose = () => {
        updateRelayStatus('offline', 'Relay Reconnecting...');
        if (telemetryRelay) telemetryRelay.textContent = 'Disconnected';
        setTimeout(connectRelayWebSocket, 3000);
      };

      ws.onerror = (err) => {
        console.warn('[Dashboard WS Error]', err);
        updateRelayStatus('offline', 'Relay Offline');
      };
    } catch (e) {
      console.error('[WS Init Error]', e);
    }
  }

  function handleWebSocketMessage(msg) {
    if (!msg || !msg.type) return;

    switch (msg.type) {
      case 'pairing_code_created':
        if (pairingCodeVal && msg.code) {
          // Format as spaced digits: "8 4 9 2 0 1"
          pairingCodeVal.textContent = msg.code.split('').join(' ');
          startCountdown(msg.expiresInSeconds || 300);
          addAuditLog('PAIRING', `Dynamic 6-digit pairing code created: ${msg.code}`, 'info');
        }
        break;

      case 'pairing_completed':
        showToast(`Phone '${msg.phoneDeviceId || 'Android Device'}' paired successfully!`, 'success');
        addAuditLog('PAIRING SUCCESS', `1-to-1 Device Bound to mobile: ${msg.phoneDeviceId}`, 'success');
        break;

      case 'status':
        if (msg.status === 'online') {
          addAuditLog('STATUS', `Laptop agent ${msg.laptopId || 'ABHINAV'} reported ONLINE`, 'success');
        }
        break;

      case 'command_result':
        showToast(`Command ${msg.command} finished (Success: ${msg.success})`, msg.success ? 'success' : 'danger');
        addAuditLog('EXECUTION', `Action '${msg.command}' executed by laptop agent. Result: ${msg.message || 'OK'}`, msg.success ? 'success' : 'danger');
        break;

      case 'pong':
        break;
    }
  }

  function updateRelayStatus(status, text) {
    if (!relayPill || !relayDot || !relayText) return;
    relayText.textContent = text;
    if (status === 'online') {
      relayPill.style.borderColor = 'rgba(16, 185, 129, 0.4)';
      relayPill.style.color = '#10b981';
      relayDot.style.backgroundColor = '#10b981';
      relayDot.style.boxShadow = '0 0 10px #10b981';
    } else if (status === 'connecting') {
      relayPill.style.borderColor = 'rgba(245, 158, 11, 0.4)';
      relayPill.style.color = '#f59e0b';
      relayDot.style.backgroundColor = '#f59e0b';
      relayDot.style.boxShadow = '0 0 10px #f59e0b';
    } else {
      relayPill.style.borderColor = 'rgba(239, 68, 68, 0.4)';
      relayPill.style.color = '#ef4444';
      relayDot.style.backgroundColor = '#ef4444';
      relayDot.style.boxShadow = '0 0 10px #ef4444';
    }
  }

  // 7. Dynamic Pairing PIN Generation & Countdown
  function requestNewPairingPin() {
    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({
        type: 'request_pairing_code',
        deviceId: 'ABHINAV'
      }));
    } else {
      // Fallback local visual demonstration if offline
      const mockCode = Math.floor(100000 + Math.random() * 900000).toString();
      pairingCodeVal.textContent = mockCode.split('').join(' ');
      startCountdown(300);
      addAuditLog('PAIRING', `Dynamic 6-digit pairing code created: ${mockCode}`, 'info');
    }
  }

  function startCountdown(seconds) {
    if (pinTimerInterval) clearInterval(pinTimerInterval);
    remainingSeconds = seconds;
    const totalSeconds = seconds;

    function renderTime() {
      const mins = Math.floor(remainingSeconds / 60);
      const secs = remainingSeconds % 60;
      if (pairingCountdown) {
        pairingCountdown.textContent = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
      }
      if (pairingTimerBar) {
        const percent = Math.max(0, (remainingSeconds / totalSeconds) * 100);
        pairingTimerBar.style.width = `${percent}%`;
      }

      if (remainingSeconds <= 0) {
        clearInterval(pinTimerInterval);
        if (pairingCountdown) pairingCountdown.textContent = 'EXPIRED';
        if (pairingCodeVal) pairingCodeVal.textContent = '- - - - - -';
        addAuditLog('PAIRING', 'Pairing code expired. Please generate a new code.', 'warning');
      }
      remainingSeconds--;
    }

    renderTime();
    pinTimerInterval = setInterval(renderTime, 1000);
  }

  if (btnGeneratePin) {
    btnGeneratePin.addEventListener('click', () => {
      requestNewPairingPin();
      showToast('Requested new 6-digit pairing PIN from relay.', 'info');
    });
  }

  if (btnCopyPairing) {
    btnCopyPairing.addEventListener('click', () => {
      const code = pairingCodeVal.textContent.replace(/\s+/g, '');
      if (code && code !== '------') {
        navigator.clipboard.writeText(code).then(() => {
          showToast(`Copied pairing code ${code} to clipboard!`, 'success');
        });
      }
    });
  }

  // 8. Workstation Power Actions & Safety Confirmation Modal
  if (actionLock) {
    actionLock.addEventListener('click', () => {
      dispatchPowerAction('LOCK');
    });
  }

  if (actionSleep) {
    actionSleep.addEventListener('click', () => {
      dispatchPowerAction('SLEEP');
    });
  }

  if (actionRestart) {
    actionRestart.addEventListener('click', () => {
      openSafetyModal('RESTART', 'Restart System', 'Are you sure you want to REBOOT laptop ABHINAV? Make sure unsaved documents are saved.');
    });
  }

  if (actionShutdown) {
    actionShutdown.addEventListener('click', () => {
      openSafetyModal('SHUTDOWN', 'Shut Down System', 'Are you sure you want to completely POWER OFF laptop ABHINAV? You will need to turn it back on physically.');
    });
  }

  function openSafetyModal(action, title, description) {
    pendingModalAction = action;
    if (modalTitle) modalTitle.textContent = title;
    if (modalDesc) modalDesc.textContent = description;
    if (safetyModal) safetyModal.classList.add('active');
  }

  function closeSafetyModal() {
    pendingModalAction = null;
    if (safetyModal) safetyModal.classList.remove('active');
  }

  if (modalBtnCancel) {
    modalBtnCancel.addEventListener('click', closeSafetyModal);
  }

  if (modalBtnConfirm) {
    modalBtnConfirm.addEventListener('click', () => {
      if (pendingModalAction) {
        dispatchPowerAction(pendingModalAction);
      }
      closeSafetyModal();
    });
  }

  function dispatchPowerAction(command) {
    addAuditLog('COMMAND', `Dispatched action '${command}' to Windows Agent (ABHINAV)`, 'info');
    showToast(`Dispatched command: ${command}`, 'info');

    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({
        type: 'command',
        command: command,
        targetDeviceId: 'ABHINAV',
        requestId: 'req-' + Date.now(),
        timestamp: new Date().toISOString()
      }));
    }
  }

  // 9. Live Audit Log Appender
  function addAuditLog(tag, message, severity = 'info') {
    if (!auditList) return;

    const item = document.createElement('div');
    item.className = `audit-item ${severity}`;

    const now = new Date();
    const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });

    item.innerHTML = `
      <span><strong>[${tag}]</strong> ${message}</span>
      <span style="color: var(--text-dim); font-size: 0.78rem;">${timeStr}</span>
    `;

    auditList.prepend(item);

    // Keep log to max 30 entries
    while (auditList.children.length > 30) {
      auditList.removeChild(auditList.lastChild);
    }
  }

  if (btnClearLogs) {
    btnClearLogs.addEventListener('click', () => {
      if (auditList) {
        auditList.innerHTML = `
          <div class="audit-item info">
            <span><strong>[CLEARED]</strong> Audit history cleared by user</span>
            <span style="color: var(--text-dim); font-size: 0.78rem;">Just now</span>
          </div>
        `;
      }
    });
  }

  // 10. Toast Notification System
  function showToast(message, type = 'info') {
    const container = document.getElementById('toast-container');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;

    let icon = 'ℹ️';
    if (type === 'success') icon = '✅';
    if (type === 'warning') icon = '⚠️';
    if (type === 'danger') icon = '🛑';

    toast.innerHTML = `<span>${icon}</span> <span>${message}</span>`;
    container.appendChild(toast);

    setTimeout(() => toast.classList.add('show'), 10);
    setTimeout(() => {
      toast.classList.remove('show');
      setTimeout(() => toast.remove(), 350);
    }, 4000);
  }
}
