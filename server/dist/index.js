"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const http_1 = __importDefault(require("http"));
const fs_1 = __importDefault(require("fs"));
const path_1 = __importDefault(require("path"));
const ws_1 = require("ws");
const deviceManager_1 = require("./deviceManager");
const PORT = parseInt(process.env.PORT || '8080', 10);
const PUBLIC_DIR = path_1.default.resolve(__dirname, '../public');
const deviceManager = new deviceManager_1.DeviceManager();
// Create HTTP server to serve the landing page, user guide, and health check API
const server = http_1.default.createServer((req, res) => {
    const parsedUrl = new URL(req.url || '/', `http://${req.headers.host || 'localhost'}`);
    const pathname = parsedUrl.pathname;
    // 1. Health & Status Check Endpoint
    if (pathname === '/health' || pathname === '/api/health') {
        res.writeHead(200, {
            'Content-Type': 'application/json',
            'Access-Control-Allow-Origin': '*'
        });
        res.end(JSON.stringify({
            status: 'healthy',
            server: 'Remote Laptop Control Relay',
            port: PORT,
            laptopsOnline: deviceManager.getOnlineLaptopsCount(),
            phonesConnected: deviceManager.getConnectedPhonesCount(),
            timestamp: new Date().toISOString()
        }));
        return;
    }
    // 2. Direct Download Shortcuts
    if (pathname === '/download/laptop' || pathname === '/download/windows') {
        res.writeHead(302, { Location: '/downloads/RemoteLaptopAgent.exe' });
        res.end();
        return;
    }
    if (pathname === '/download/laptop-zip' || pathname === '/download/windows-zip') {
        res.writeHead(302, { Location: '/downloads/RemoteLaptopAgent-Windows.zip' });
        res.end();
        return;
    }
    if (pathname === '/download/mobile' || pathname === '/download/android') {
        res.writeHead(302, { Location: '/downloads/RemoteLaptopControl.apk' });
        res.end();
        return;
    }
    // 3. Static File Serving from public/
    let reqPath = pathname === '/' ? 'index.html' : pathname;
    if (reqPath === '/dashboard' || reqPath === '/laptop') {
        reqPath = 'dashboard.html';
    }
    let filePath = path_1.default.join(PUBLIC_DIR, reqPath);
    // Security: Prevent directory traversal
    if (!filePath.startsWith(PUBLIC_DIR)) {
        res.writeHead(403, { 'Content-Type': 'text/plain' });
        res.end('Forbidden');
        return;
    }
    if (fs_1.default.existsSync(filePath) && fs_1.default.statSync(filePath).isFile()) {
        const ext = path_1.default.extname(filePath).toLowerCase();
        const mimeTypes = {
            '.html': 'text/html; charset=utf-8',
            '.css': 'text/css; charset=utf-8',
            '.js': 'application/javascript; charset=utf-8',
            '.json': 'application/json',
            '.png': 'image/png',
            '.jpg': 'image/jpeg',
            '.jpeg': 'image/jpeg',
            '.svg': 'image/svg+xml',
            '.ico': 'image/x-icon',
            '.apk': 'application/vnd.android.package-archive',
            '.zip': 'application/zip',
            '.exe': 'application/octet-stream'
        };
        const contentType = mimeTypes[ext] || 'application/octet-stream';
        const headers = { 'Content-Type': contentType };
        if (ext === '.apk' || ext === '.exe' || ext === '.zip') {
            headers['Content-Disposition'] = `attachment; filename="${path_1.default.basename(filePath)}"`;
        }
        res.writeHead(200, headers);
        fs_1.default.createReadStream(filePath).pipe(res);
        return;
    }
    // Fallback to index.html for SPA / client routes
    const indexHtml = path_1.default.join(PUBLIC_DIR, 'index.html');
    if (fs_1.default.existsSync(indexHtml)) {
        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        fs_1.default.createReadStream(indexHtml).pipe(res);
        return;
    }
    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('Not Found');
});
// Attach WebSocketServer to the HTTP server
const wss = new ws_1.WebSocketServer({ server });
// Track ping/pong health for zombie socket detection
const aliveSockets = new WeakSet();
console.log('====================================================');
console.log('  Remote Laptop Control - Relay & Web Server       ');
console.log('====================================================');
console.log(`[Server] Web & Relay Server running at http://localhost:${PORT}`);
console.log(`[Auth] Default dev secret: ${process.env.AGENT_SECRET || 'dev-secret-token-123'}`);
console.log('Waiting for web users, laptops, and phone connections...\n');
wss.on('connection', (ws, req) => {
    const clientIp = req.socket.remoteAddress || 'unknown';
    console.log(`[Connection] New WebSocket connected from ${clientIp}`);
    aliveSockets.add(ws);
    // Native WebSocket pong listener
    ws.on('pong', () => {
        aliveSockets.add(ws);
    });
    ws.on('message', (raw) => {
        aliveSockets.add(ws);
        let message;
        try {
            message = JSON.parse(raw.toString());
        }
        catch {
            deviceManager.sendMessage(ws, {
                type: 'error',
                message: 'Invalid JSON payload received.'
            });
            return;
        }
        if (!message || typeof message.type !== 'string') {
            deviceManager.sendMessage(ws, {
                type: 'error',
                message: 'Malformed message: missing "type" property.'
            });
            return;
        }
        switch (message.type) {
            case 'auth': {
                const result = deviceManager.authenticate(ws, message.deviceId, message.deviceType, message.token);
                deviceManager.sendMessage(ws, {
                    type: 'auth_result',
                    success: result.success,
                    message: result.message,
                    deviceId: message.deviceId
                });
                break;
            }
            case 'command': {
                deviceManager.routeCommand(ws, message);
                break;
            }
            case 'command_result': {
                deviceManager.routeCommandResult(ws, message);
                break;
            }
            case 'request_pairing_code': {
                deviceManager.handleRequestPairingCode(ws, message.deviceId);
                break;
            }
            case 'pair_with_code': {
                deviceManager.handlePairWithCode(ws, message.phoneDeviceId, message.code);
                break;
            }
            case 'ping': {
                deviceManager.sendMessage(ws, { type: 'pong' });
                break;
            }
            case 'pong': {
                // Application-level pong handled
                break;
            }
            default: {
                deviceManager.sendMessage(ws, {
                    type: 'error',
                    message: `Unknown message type: ${message.type}`
                });
                break;
            }
        }
    });
    ws.on('close', (code, reason) => {
        console.log(`[Connection] Closed (Code: ${code}, Reason: ${reason.toString() || 'none'})`);
        deviceManager.handleDisconnect(ws);
    });
    ws.on('error', (err) => {
        console.error(`[Connection Error] ${err.message}`);
        deviceManager.handleDisconnect(ws);
    });
});
// Start listening on HTTP + WebSocket
server.listen(PORT, () => {
    console.log(`[Server] HTTP & WebSocket Server active on port ${PORT}`);
});
// Periodic heartbeat to clean up broken/zombie connections (every 30s)
const heartbeatInterval = setInterval(() => {
    wss.clients.forEach((ws) => {
        if (!aliveSockets.has(ws)) {
            console.log('[Heartbeat] Terminating inactive socket (no pong received)');
            return ws.terminate();
        }
        aliveSockets.delete(ws);
        ws.ping();
    });
}, 30000);
// Graceful process shutdown
const cleanup = () => {
    console.log('\n[Server] Shutting down WebSocket and Web server...');
    clearInterval(heartbeatInterval);
    wss.close(() => {
        server.close(() => {
            console.log('[Server] Closed all connections. Goodbye.');
            process.exit(0);
        });
    });
};
process.on('SIGINT', cleanup);
process.on('SIGTERM', cleanup);
