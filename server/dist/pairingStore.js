"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.PairingStore = void 0;
const fs_1 = __importDefault(require("fs"));
const path_1 = __importDefault(require("path"));
const crypto_1 = __importDefault(require("crypto"));
class PairingStore {
    filePath;
    pairings = new Map();
    pendingPairings = new Map();
    defaultSecretToken = process.env.AGENT_SECRET || 'dev-secret-token-123';
    constructor(dataDir) {
        const dir = dataDir || path_1.default.resolve(__dirname, '../data');
        if (!fs_1.default.existsSync(dir)) {
            fs_1.default.mkdirSync(dir, { recursive: true });
        }
        this.filePath = path_1.default.join(dir, 'pairings.json');
        this.load();
    }
    load() {
        try {
            if (fs_1.default.existsSync(this.filePath)) {
                const raw = fs_1.default.readFileSync(this.filePath, 'utf-8');
                const list = JSON.parse(raw);
                for (const p of list) {
                    this.pairings.set(p.token, p);
                }
                console.log(`[PairingStore] Loaded ${this.pairings.size} paired devices from disk.`);
            }
        }
        catch (err) {
            console.error(`[PairingStore] Error loading pairings: ${err.message}`);
        }
    }
    save() {
        try {
            const list = Array.from(this.pairings.values());
            fs_1.default.writeFileSync(this.filePath, JSON.stringify(list, null, 2), 'utf-8');
        }
        catch (err) {
            console.error(`[PairingStore] Error saving pairings: ${err.message}`);
        }
    }
    validateToken(token, deviceId, deviceType) {
        // Only permit fallback dev secret if no pairings exist at all (initial bootstrapping)
        if (this.pairings.size === 0 && token === this.defaultSecretToken) {
            return true;
        }
        const paired = this.pairings.get(token);
        if (!paired)
            return false;
        return deviceType === 'laptop' ? paired.laptopId === deviceId : paired.phoneId === deviceId;
    }
    getPairingByToken(token) {
        return this.pairings.get(token);
    }
    getPairingForLaptop(laptopId) {
        for (const p of this.pairings.values()) {
            if (p.laptopId === laptopId)
                return p;
        }
        return undefined;
    }
    getPairingForPhone(phoneId) {
        for (const p of this.pairings.values()) {
            if (p.phoneId === phoneId)
                return p;
        }
        return undefined;
    }
    createPairingCode(laptopSocket, laptopId) {
        // Invalidate previous pending codes for this laptop
        for (const [c, p] of this.pendingPairings.entries()) {
            if (p.laptopId === laptopId || Date.now() > p.expiresAt) {
                this.pendingPairings.delete(c);
            }
        }
        // Generate random 6-digit numeric PIN
        const code = Math.floor(100000 + Math.random() * 900000).toString();
        const expiresInSeconds = 300; // 5 minutes
        this.pendingPairings.set(code, {
            code,
            laptopSocket,
            laptopId,
            expiresAt: Date.now() + expiresInSeconds * 1000
        });
        console.log(`[PairingStore] Generated pairing code ${code} for laptop ${laptopId} (valid for 5m)`);
        return { code, expiresInSeconds };
    }
    completePairing(code, phoneId) {
        const pending = this.pendingPairings.get(code);
        if (!pending) {
            return { success: false, error: 'Invalid pairing code. Please generate a new code on your laptop.' };
        }
        if (Date.now() > pending.expiresAt) {
            this.pendingPairings.delete(code);
            return { success: false, error: 'Pairing code has expired. Please generate a new code.' };
        }
        // Generate 256-bit secure cryptographic token
        const token = 'rlc_' + crypto_1.default.randomBytes(24).toString('hex');
        const paired = {
            token,
            laptopId: pending.laptopId,
            phoneId,
            pairedAt: new Date().toISOString()
        };
        // Enforce strict 1-to-1: remove previous pairings for this laptop
        for (const [t, p] of this.pairings.entries()) {
            if (p.laptopId === pending.laptopId) {
                this.pairings.delete(t);
            }
        }
        this.pairings.set(token, paired);
        this.save();
        this.pendingPairings.delete(code);
        console.log(`[PairingStore] Successfully paired Phone (${phoneId}) with Laptop (${pending.laptopId})`);
        return {
            success: true,
            token,
            laptopId: pending.laptopId,
            laptopSocket: pending.laptopSocket
        };
    }
}
exports.PairingStore = PairingStore;
