"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.DeviceManager = void 0;
const ws_1 = require("ws");
const pairingStore_1 = require("./pairingStore");
class DeviceManager {
    // Isolated multi-device maps keyed by deviceId
    laptops = new Map();
    phones = new Map();
    socketToSession = new Map();
    pairingStore = new pairingStore_1.PairingStore();
    /**
     * Safely serializes and sends a message to a WebSocket if it is currently open.
     */
    sendMessage(socket, message) {
        if (socket.readyState === ws_1.WebSocket.OPEN) {
            socket.send(JSON.stringify(message));
            return true;
        }
        return false;
    }
    /**
     * Authenticates a newly connected device with strict pairing token validation.
     */
    authenticate(socket, deviceId, deviceType, token) {
        if (!token || !this.pairingStore.validateToken(token, deviceId, deviceType)) {
            return { success: false, message: 'Invalid or unpaired authentication credential.' };
        }
        if (!deviceId || !deviceType) {
            return { success: false, message: 'Missing deviceId or deviceType.' };
        }
        // Lookup paired peer for this specific token
        const pairing = this.pairingStore.getPairingByToken(token);
        const pairedPeerId = pairing
            ? (deviceType === 'laptop' ? pairing.phoneId : pairing.laptopId)
            : undefined;
        const session = {
            deviceId,
            deviceType,
            socket,
            token,
            pairedPeerId,
            authenticatedAt: new Date()
        };
        if (deviceType === 'laptop') {
            // If this specific laptop reconnected, close the previous socket cleanly
            const existing = this.laptops.get(deviceId);
            if (existing && existing.socket !== socket) {
                this.socketToSession.delete(existing.socket);
                existing.socket.close(1000, 'Replaced by new connection');
            }
            this.laptops.set(deviceId, session);
            this.socketToSession.set(socket, session);
            console.log(`[DeviceManager] Laptop connected and authenticated: ${deviceId} (paired with phone: ${pairedPeerId || 'none'})`);
            // Notify ONLY the paired phone that this laptop is ONLINE
            if (pairedPeerId) {
                this.notifyPhoneOfLaptopStatus(pairedPeerId, deviceId, 'online');
            }
            return { success: true, message: 'Laptop authenticated successfully.' };
        }
        if (deviceType === 'phone') {
            // If this specific phone reconnected, close the previous socket cleanly
            const existing = this.phones.get(deviceId);
            if (existing && existing.socket !== socket) {
                this.socketToSession.delete(existing.socket);
                existing.socket.close(1000, 'Replaced by new connection');
            }
            this.phones.set(deviceId, session);
            this.socketToSession.set(socket, session);
            console.log(`[DeviceManager] Phone connected and authenticated: ${deviceId} (paired with laptop: ${pairedPeerId || 'none'})`);
            // Send paired laptop status right after auth_result
            if (pairedPeerId) {
                setImmediate(() => {
                    const isOnline = this.isLaptopOnline(pairedPeerId);
                    this.sendMessage(socket, {
                        type: 'status',
                        status: isOnline ? 'online' : 'offline',
                        laptopId: pairedPeerId,
                        timestamp: new Date().toISOString()
                    });
                });
            }
            return { success: true, message: 'Phone authenticated successfully.' };
        }
        return { success: false, message: `Unknown device type: ${deviceType}` };
    }
    /**
     * Routes a control command from Phone A strictly to its paired Laptop A.
     * Phone B, C, or any unpaired phone is strictly blocked.
     */
    routeCommand(fromSocket, command) {
        const sender = this.socketToSession.get(fromSocket);
        if (!sender || sender.deviceType !== 'phone') {
            this.sendMessage(fromSocket, {
                type: 'error',
                message: 'Unauthorized: Only an authenticated phone can send commands.'
            });
            return;
        }
        const pairedLaptopId = sender.pairedPeerId;
        if (!pairedLaptopId) {
            this.sendMessage(fromSocket, {
                type: 'error',
                message: 'Unauthorized: Phone is not paired with any laptop.'
            });
            return;
        }
        // Find the SPECIFIC paired laptop session
        const targetLaptop = this.laptops.get(pairedLaptopId);
        if (!targetLaptop || targetLaptop.socket.readyState !== ws_1.WebSocket.OPEN) {
            this.sendMessage(fromSocket, {
                type: 'error',
                message: `Cannot execute command: Paired laptop (${pairedLaptopId}) is currently offline.`
            });
            return;
        }
        // STRICT TOKEN ISOLATION: Phone and Laptop tokens MUST match
        if (targetLaptop.token !== sender.token) {
            this.sendMessage(fromSocket, {
                type: 'error',
                message: 'Security Violation: Authentication token does not match target laptop.'
            });
            return;
        }
        console.log(`[Router] Routing command '${command.command}' (ID: ${command.requestId}) from Phone (${sender.deviceId}) -> Paired Laptop (${pairedLaptopId})`);
        this.sendMessage(targetLaptop.socket, command);
    }
    /**
     * Routes a command execution result from Laptop A strictly to its paired Phone A.
     */
    routeCommandResult(fromSocket, result) {
        const sender = this.socketToSession.get(fromSocket);
        if (!sender || sender.deviceType !== 'laptop') {
            this.sendMessage(fromSocket, {
                type: 'error',
                message: 'Unauthorized: Only an authenticated laptop can report results.'
            });
            return;
        }
        const pairedPhoneId = sender.pairedPeerId;
        if (!pairedPhoneId) {
            console.log(`[Router] Laptop ${sender.deviceId} returned result, but has no paired phone.`);
            return;
        }
        const targetPhone = this.phones.get(pairedPhoneId);
        if (!targetPhone || targetPhone.socket.readyState !== ws_1.WebSocket.OPEN) {
            console.log(`[Router] Laptop ${sender.deviceId} returned result, but paired phone (${pairedPhoneId}) is offline.`);
            return;
        }
        // STRICT TOKEN ISOLATION
        if (targetPhone.token !== sender.token) {
            console.log(`[Router] Security mismatch: Laptop and phone tokens do not match.`);
            return;
        }
        console.log(`[Router] Routing command result for '${result.command}' (Success: ${result.success}) -> Paired Phone (${pairedPhoneId})`);
        this.sendMessage(targetPhone.socket, result);
    }
    /**
     * Handles socket disconnection and notifies ONLY paired peers.
     */
    handleDisconnect(socket) {
        const session = this.socketToSession.get(socket);
        if (!session)
            return;
        this.socketToSession.delete(socket);
        if (session.deviceType === 'laptop') {
            const existing = this.laptops.get(session.deviceId);
            if (existing?.socket === socket) {
                this.laptops.delete(session.deviceId);
                console.log(`[DeviceManager] Laptop disconnected: ${session.deviceId}`);
                // Notify ONLY the paired phone
                if (session.pairedPeerId) {
                    this.notifyPhoneOfLaptopStatus(session.pairedPeerId, session.deviceId, 'offline');
                }
            }
        }
        else if (session.deviceType === 'phone') {
            const existing = this.phones.get(session.deviceId);
            if (existing?.socket === socket) {
                this.phones.delete(session.deviceId);
                console.log(`[DeviceManager] Phone disconnected: ${session.deviceId}`);
            }
        }
    }
    getSession(socket) {
        return this.socketToSession.get(socket);
    }
    isLaptopOnline(laptopId) {
        if (!laptopId) {
            return this.laptops.size > 0;
        }
        const laptop = this.laptops.get(laptopId);
        return laptop !== undefined && laptop.socket.readyState === ws_1.WebSocket.OPEN;
    }
    getOnlineLaptopsCount() {
        return this.laptops.size;
    }
    getConnectedPhonesCount() {
        return this.phones.size;
    }
    notifyPhoneOfLaptopStatus(phoneId, laptopId, status) {
        const phone = this.phones.get(phoneId);
        if (phone && phone.socket.readyState === ws_1.WebSocket.OPEN) {
            const statusMsg = {
                type: 'status',
                status,
                laptopId,
                timestamp: new Date().toISOString()
            };
            this.sendMessage(phone.socket, statusMsg);
        }
    }
    /**
     * Generates a 6-digit numeric pairing code for an unauthenticated or authenticated laptop.
     */
    handleRequestPairingCode(socket, laptopId) {
        const { code, expiresInSeconds } = this.pairingStore.createPairingCode(socket, laptopId);
        this.sendMessage(socket, {
            type: 'pairing_code_created',
            code,
            expiresInSeconds
        });
    }
    /**
     * Processes a phone pairing attempt using a 6-digit PIN code.
     */
    handlePairWithCode(phoneSocket, phoneDeviceId, code) {
        const result = this.pairingStore.completePairing(code, phoneDeviceId);
        if (!result.success || !result.token || !result.laptopId) {
            this.sendMessage(phoneSocket, {
                type: 'pairing_failed',
                success: false,
                message: result.error || 'Pairing failed. Invalid or expired code.'
            });
            return;
        }
        // 1. Notify the phone of successful pairing
        this.sendMessage(phoneSocket, {
            type: 'pairing_success',
            success: true,
            token: result.token,
            laptopId: result.laptopId
        });
        // 2. Notify the laptop client that pairing is completed and provide token
        if (result.laptopSocket && result.laptopSocket.readyState === ws_1.WebSocket.OPEN) {
            this.sendMessage(result.laptopSocket, {
                type: 'pairing_completed',
                token: result.token,
                phoneDeviceId
            });
            // Authenticate the laptop with the new token
            this.authenticate(result.laptopSocket, result.laptopId, 'laptop', result.token);
        }
        // 3. Authenticate the phone immediately
        this.authenticate(phoneSocket, phoneDeviceId, 'phone', result.token);
    }
}
exports.DeviceManager = DeviceManager;
