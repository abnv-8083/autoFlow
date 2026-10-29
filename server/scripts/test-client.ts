import { WebSocket } from 'ws';

const mode = process.argv[2] || 'laptop';
const SERVER_URL = process.env.SERVER_URL || 'ws://localhost:8080';
const SECRET_TOKEN = process.env.AGENT_SECRET || 'dev-secret-token-123';

console.log(`Starting simulated [${mode.toUpperCase()}] client...`);
console.log(`Connecting to ${SERVER_URL}...`);

const ws = new WebSocket(SERVER_URL);

ws.on('open', () => {
  console.log(`[Connected] WebSocket open. Authenticating as ${mode}...`);

  // Send Auth message
  const authPayload = {
    type: 'auth',
    deviceId: mode === 'laptop' ? 'my-windows-laptop' : 'my-android-phone',
    deviceType: mode,
    token: SECRET_TOKEN
  };

  ws.send(JSON.stringify(authPayload));
});

ws.on('message', (data: Buffer) => {
  const msg = JSON.parse(data.toString());
  console.log(`\n[Received ${msg.type}] ->`, JSON.stringify(msg, null, 2));

  // If this is the phone and we received laptop status online, send a test command!
  if (mode === 'phone' && msg.type === 'status' && msg.status === 'online') {
    console.log('\n[Action] Laptop is ONLINE! Sending test LOCK command in 1 second...');
    setTimeout(() => {
      const commandPayload = {
        type: 'command',
        requestId: 'req-' + Date.now(),
        command: 'LOCK'
      };
      console.log('[Sending Command] ->', JSON.stringify(commandPayload));
      ws.send(JSON.stringify(commandPayload));
    }, 1000);
  }

  // If this is the laptop and we received a command, send a simulated success result!
  if (mode === 'laptop' && msg.type === 'command') {
    console.log(`\n[Action] Laptop received command: ${msg.command}. Returning success result...`);
    const resultPayload = {
      type: 'command_result',
      requestId: msg.requestId,
      command: msg.command,
      success: true
    };
    ws.send(JSON.stringify(resultPayload));
  }
});

ws.on('close', (code, reason) => {
  console.log(`[Disconnected] Code: ${code}, Reason: ${reason.toString() || 'none'}`);
  process.exit(0);
});

ws.on('error', (err) => {
  console.error(`[Error] ${err.message}`);
});
