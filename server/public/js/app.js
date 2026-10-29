/**
 * Remote Laptop Control - Landing Page & Interactive Guide App
 */

document.addEventListener('DOMContentLoaded', () => {
  initLiveHealthCheck();
  initTabs();
  initCopyButtons();
  initWebSocketDiagnostic();
  initPairingSimulator();
  initDownloadNotifications();
});

// 1. Live Server Health Poller
function initLiveHealthCheck() {
  const statusBadge = document.getElementById('relay-status-pill');
  const statusDot = document.getElementById('relay-status-dot');
  const statusText = document.getElementById('relay-status-text');
  const metricLaptops = document.getElementById('metric-laptops');
  const metricPhones = document.getElementById('metric-phones');
  const diagServerPort = document.getElementById('diag-port');
  const diagLatency = document.getElementById('diag-latency');

  async function checkHealth() {
    const startTime = performance.now();
    try {
      const response = await fetch('/health');
      const data = await response.json();
      const latency = Math.round(performance.now() - startTime);

      if (data.status === 'healthy') {
        if (statusText) statusText.textContent = 'Relay Server Online';
        if (statusBadge) {
          statusBadge.style.borderColor = 'rgba(16, 185, 129, 0.4)';
          statusBadge.style.color = '#10b981';
        }
        if (statusDot) {
          statusDot.style.backgroundColor = '#10b981';
          statusDot.style.boxShadow = '0 0 10px #10b981';
        }
        if (metricLaptops) metricLaptops.textContent = data.laptopsOnline || '0';
        if (metricPhones) metricPhones.textContent = data.phonesConnected || '0';
        if (diagServerPort) diagServerPort.textContent = data.port || '8080';
        if (diagLatency) diagLatency.textContent = `${latency}ms`;
      }
    } catch (err) {
      if (statusText) statusText.textContent = 'Connecting to Relay...';
      if (statusBadge) {
        statusBadge.style.borderColor = 'rgba(245, 158, 11, 0.4)';
        statusBadge.style.color = '#f59e0b';
      }
      if (statusDot) {
        statusDot.style.backgroundColor = '#f59e0b';
        statusDot.style.boxShadow = '0 0 10px #f59e0b';
      }
    }
  }

  checkHealth();
  setInterval(checkHealth, 5000);
}

// 2. Interactive Guide Tabs
function initTabs() {
  const tabBtns = document.querySelectorAll('.tab-btn');
  const tabContents = document.querySelectorAll('.tab-content');

  tabBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      const targetId = btn.getAttribute('data-tab');

      tabBtns.forEach(b => b.classList.remove('active'));
      tabContents.forEach(c => c.classList.remove('active'));

      btn.classList.add('active');
      const targetContent = document.getElementById(targetId);
      if (targetContent) targetContent.classList.add('active');
    });
  });
}

// 3. One-Click Clipboard Copy
function initCopyButtons() {
  const copyButtons = document.querySelectorAll('.copy-btn');

  copyButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const targetId = btn.getAttribute('data-copy-target');
      const targetElement = document.getElementById(targetId);

      if (targetElement) {
        const textToCopy = targetElement.textContent.trim();
        navigator.clipboard.writeText(textToCopy).then(() => {
          const originalText = btn.textContent;
          btn.textContent = 'Copied!';
          btn.style.color = '#10b981';

          setTimeout(() => {
            btn.textContent = originalText;
            btn.style.color = '';
          }, 2000);
        });
      }
    });
  });
}

// 4. Live WebSocket Diagnostic & Latency Tester
function initWebSocketDiagnostic() {
  const testBtn = document.getElementById('btn-test-ws');
  const logOutput = document.getElementById('ws-test-log');
  const wsStatusVal = document.getElementById('diag-ws-status');

  if (!testBtn || !logOutput) return;

  testBtn.addEventListener('click', () => {
    logOutput.textContent = 'Initiating WebSocket connection to relay server...\n';
    testBtn.disabled = true;
    testBtn.textContent = 'Testing...';

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${window.location.host}`;

    const startTime = performance.now();
    let socket;

    try {
      socket = new WebSocket(wsUrl);

      socket.onopen = () => {
        const connectTime = Math.round(performance.now() - startTime);
        logOutput.textContent += `[Connected] Handshake complete in ${connectTime}ms.\n`;
        logOutput.textContent += `[Send] Transmitting test ping heartbeat...\n`;
        socket.send(JSON.stringify({ type: 'ping' }));
      };

      socket.onmessage = (event) => {
        try {
          const msg = JSON.parse(event.data);
          const rtt = Math.round(performance.now() - startTime);
          logOutput.textContent += `[Received] ${msg.type.toUpperCase()} frame acknowledged. RTT Latency: ${rtt}ms.\n`;
          logOutput.textContent += `[Success] WebSocket relay is fully operational and responsive!\n`;

          if (wsStatusVal) {
            wsStatusVal.textContent = 'ACTIVE (100%)';
            wsStatusVal.style.color = '#10b981';
          }
          socket.close();
        } catch {
          logOutput.textContent += `[Data] Received raw frame: ${event.data}\n`;
        }
      };

      socket.onerror = (err) => {
        logOutput.textContent += `[Error] WebSocket connection failed. Verify server is running.\n`;
        if (wsStatusVal) {
          wsStatusVal.textContent = 'FAILED';
          wsStatusVal.style.color = '#ef4444';
        }
      };

      socket.onclose = () => {
        logOutput.textContent += `[Closed] Socket cleanly disconnected.\n`;
        testBtn.disabled = false;
        testBtn.textContent = 'Run Test Again';
      };

      setTimeout(() => {
        if (socket.readyState === WebSocket.CONNECTING) {
          socket.close();
          logOutput.textContent += `[Timeout] Connection timed out after 5000ms.\n`;
          testBtn.disabled = false;
          testBtn.textContent = 'Retry Test';
        }
      }, 5000);

    } catch (e) {
      logOutput.textContent += `[Exception] Failed to instantiate WebSocket: ${e.message}\n`;
      testBtn.disabled = false;
      testBtn.textContent = 'Retry Test';
    }
  });
}

// 5. Interactive Pairing Simulator
function initPairingSimulator() {
  const pinDigits = document.querySelectorAll('.sim-pin-digit');
  const btnGenPin = document.getElementById('btn-sim-gen-pin');
  const simStatus = document.getElementById('sim-pairing-status');
  const simTokenBox = document.getElementById('sim-token-result');
  const simTokenVal = document.getElementById('sim-token-val');

  if (!btnGenPin) return;

  btnGenPin.addEventListener('click', () => {
    // Generate a random 6-digit PIN
    const randomPin = Math.floor(100000 + Math.random() * 900000).toString();
    
    // Animate typing digits
    randomPin.split('').forEach((digit, idx) => {
      setTimeout(() => {
        if (pinDigits[idx]) {
          pinDigits[idx].textContent = digit;
          pinDigits[idx].style.borderColor = '#3b82f6';
          pinDigits[idx].style.color = '#38bdf8';
        }
      }, idx * 100);
    });

    if (simStatus) {
      simStatus.textContent = 'Pairing code generated! Exchanging 256-bit cryptographic token...';
      simStatus.style.color = '#f59e0b';
    }

    // Simulate server cryptographic handshake completion after 1.2s
    setTimeout(() => {
      if (simStatus) {
        simStatus.textContent = 'Pairing Successful! Cryptographic token bound to device.';
        simStatus.style.color = '#10b981';
      }
      if (simTokenBox && simTokenVal) {
        simTokenVal.textContent = 'rlc_' + Array.from({length: 48}, () => Math.floor(Math.random()*16).toString(16)).join('');
        simTokenBox.style.display = 'block';
      }
    }, 1200);
  });
}

// 6. Interactive Download Notification Feedback
function initDownloadNotifications() {
  const laptopExeBtn = document.getElementById('btn-download-laptop-exe');
  const laptopZipBtn = document.getElementById('btn-download-laptop-zip');
  const apkBtn = document.getElementById('btn-download-portal-apk');

  if (laptopExeBtn) {
    laptopExeBtn.addEventListener('click', () => {
      showToast('📥 Windows Agent download started! Run with --tray to minimize to the System Tray.', 'success');
    });
  }

  if (laptopZipBtn) {
    laptopZipBtn.addEventListener('click', () => {
      showToast('📦 Windows Package download started! Extract and double-click Start-Tray.bat.', 'success');
    });
  }

  if (apkBtn) {
    apkBtn.addEventListener('click', () => {
      showToast('📱 Android APK download started! Open and install on your smartphone.', 'success');
    });
  }
}

function showToast(message, type = 'info') {
  let container = document.querySelector('.toast-container');
  if (!container) {
    container = document.createElement('div');
    container.className = 'toast-container';
    document.body.appendChild(container);
  }

  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  toast.textContent = message;

  container.appendChild(toast);

  // Trigger animation
  requestAnimationFrame(() => {
    toast.classList.add('show');
  });

  setTimeout(() => {
    toast.classList.remove('show');
    setTimeout(() => {
      if (toast.parentNode) toast.parentNode.removeChild(toast);
    }, 350);
  }, 4000);
}

