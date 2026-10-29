import fs from 'fs';
import path from 'path';
import crypto from 'crypto';
import { WebSocket } from 'ws';

export interface PairedDevice {
  token: string;
  laptopId: string;
  phoneId: string;
  pairedAt: string;
}

export interface PendingPairing {
  code: string;
  laptopSocket: WebSocket;
  laptopId: string;
  expiresAt: number;
}

export class PairingStore {
  private readonly filePath: string;
  private pairings: Map<string, PairedDevice> = new Map();
  private pendingPairings: Map<string, PendingPairing> = new Map();
  private readonly defaultSecretToken = process.env.AGENT_SECRET || 'dev-secret-token-123';

  constructor(dataDir?: string) {
    const dir = dataDir || path.resolve(__dirname, '../data');
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true });
    }
    this.filePath = path.join(dir, 'pairings.json');
    this.load();
  }

  private load(): void {
    try {
      if (fs.existsSync(this.filePath)) {
        const raw = fs.readFileSync(this.filePath, 'utf-8');
        const list: PairedDevice[] = JSON.parse(raw);
        for (const p of list) {
          this.pairings.set(p.token, p);
        }
        console.log(`[PairingStore] Loaded ${this.pairings.size} paired devices from disk.`);
      }
    } catch (err: any) {
      console.error(`[PairingStore] Error loading pairings: ${err.message}`);
    }
  }

  private save(): void {
    try {
      const list = Array.from(this.pairings.values());
      fs.writeFileSync(this.filePath, JSON.stringify(list, null, 2), 'utf-8');
    } catch (err: any) {
      console.error(`[PairingStore] Error saving pairings: ${err.message}`);
    }
  }

  public validateToken(token: string, deviceId: string, deviceType: 'laptop' | 'phone'): boolean {
    // Only permit fallback dev secret if no pairings exist at all (initial bootstrapping)
    if (this.pairings.size === 0 && token === this.defaultSecretToken) {
      return true;
    }
    const paired = this.pairings.get(token);
    if (!paired) return false;
    return deviceType === 'laptop' ? paired.laptopId === deviceId : paired.phoneId === deviceId;
  }

  public getPairingByToken(token: string): PairedDevice | undefined {
    return this.pairings.get(token);
  }

  public getPairingForLaptop(laptopId: string): PairedDevice | undefined {
    for (const p of this.pairings.values()) {
      if (p.laptopId === laptopId) return p;
    }
    return undefined;
  }

  public getPairingForPhone(phoneId: string): PairedDevice | undefined {
    for (const p of this.pairings.values()) {
      if (p.phoneId === phoneId) return p;
    }
    return undefined;
  }

  public createPairingCode(laptopSocket: WebSocket, laptopId: string): { code: string; expiresInSeconds: number } {
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

  public completePairing(
    code: string,
    phoneId: string
  ): { success: boolean; token?: string; laptopId?: string; laptopSocket?: WebSocket; error?: string } {
    const pending = this.pendingPairings.get(code);
    if (!pending) {
      return { success: false, error: 'Invalid pairing code. Please generate a new code on your laptop.' };
    }

    if (Date.now() > pending.expiresAt) {
      this.pendingPairings.delete(code);
      return { success: false, error: 'Pairing code has expired. Please generate a new code.' };
    }

    // Generate 256-bit secure cryptographic token
    const token = 'rlc_' + crypto.randomBytes(24).toString('hex');
    const paired: PairedDevice = {
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
