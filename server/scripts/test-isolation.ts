import { WebSocket } from 'ws';

const SERVER_URL = 'ws://localhost:8080';
const PAIRED_TOKEN = 'rlc_5ee061b2b06cbd890d4a5a0731283c2808d9823c2efd89c0';
const PAIRED_LAPTOP_ID = 'ABHINAV';
const PAIRED_PHONE_ID = 'android-6608db9b';

async function runTests() {
  console.log('====================================================');
  console.log('   Security & Isolation Test: Phone A vs Phone B    ');
  console.log('====================================================\n');

  let testsPassed = 0;
  let totalTests = 0;

  function assert(condition: boolean, testName: string) {
    totalTests++;
    if (condition) {
      console.log(`[PASS] ${testName}`);
      testsPassed++;
    } else {
      console.error(`[FAIL] ${testName}`);
    }
  }

  // Helper to connect and exchange messages
  function sendAndReceive(payload: any, expectedType?: string): Promise<any> {
    return new Promise((resolve) => {
      const ws = new WebSocket(SERVER_URL);
      ws.on('open', () => {
        ws.send(JSON.stringify(payload));
      });
      ws.on('message', (data: Buffer) => {
        const msg = JSON.parse(data.toString());
        if (!expectedType || msg.type === expectedType) {
          ws.close();
          resolve(msg);
        }
      });
      ws.on('error', () => {
        resolve({ error: true });
      });
      setTimeout(() => {
        ws.close();
        resolve({ timeout: true });
      }, 3000);
    });
  }

  // TEST 1: Phone B with random/fake token
  console.log('--- Test 1: Phone B connects with random invalid token ---');
  const res1 = await sendAndReceive({
    type: 'auth',
    deviceId: 'phone-B-attacker',
    deviceType: 'phone',
    token: 'fake-token-xyz'
  }, 'auth_result');
  assert(res1.type === 'auth_result' && res1.success === false, 'Phone B with fake token is REJECTED');

  // TEST 2: Phone B attempts token replay attack (using Phone A\'s token with Phone B\'s deviceId)
  console.log('\n--- Test 2: Phone B attempts token replay with Phone A\'s token ---');
  const res2 = await sendAndReceive({
    type: 'auth',
    deviceId: 'phone-B-hacker',
    deviceType: 'phone',
    token: PAIRED_TOKEN
  }, 'auth_result');
  assert(res2.type === 'auth_result' && res2.success === false, 'Phone B using Phone A token with wrong deviceId is REJECTED');

  // TEST 3: Phone B attempts dev secret bypass
  console.log('\n--- Test 3: Phone B attempts dev default token bypass ---');
  const res3 = await sendAndReceive({
    type: 'auth',
    deviceId: 'phone-B-dev-bypass',
    deviceType: 'phone',
    token: 'dev-secret-token-123'
  }, 'auth_result');
  assert(res3.type === 'auth_result' && res3.success === false, 'Phone B dev token bypass is REJECTED when pairings exist');

  // TEST 4: Phone B sends command without valid auth
  console.log('\n--- Test 4: Unauthenticated Phone B sends command ---');
  const res4 = await sendAndReceive({
    type: 'command',
    requestId: 'req-unauthorized',
    command: 'LOCK'
  });
  assert(res4.type === 'error' && res4.message.includes('Unauthorized'), 'Unauthenticated command from Phone B is REJECTED');

  // TEST 5: Legitimate Phone A authenticates
  console.log('\n--- Test 5: Legitimate Phone A authenticates with paired token ---');
  const res5 = await sendAndReceive({
    type: 'auth',
    deviceId: PAIRED_PHONE_ID,
    deviceType: 'phone',
    token: PAIRED_TOKEN
  }, 'auth_result');
  assert(res5.type === 'auth_result' && res5.success === true, 'Legitimate Phone A authentication is ACCEPTED');

  console.log('\n====================================================');
  console.log(`Results: ${testsPassed}/${totalTests} Tests Passed`);
  console.log('====================================================\n');

  if (testsPassed === totalTests) {
    console.log('[SUCCESS] Strict 1-to-1 Device Isolation is 100% verified!');
    process.exit(0);
  } else {
    console.error('[ERROR] Some isolation tests failed!');
    process.exit(1);
  }
}

runTests();
