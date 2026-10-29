import { WebSocket } from 'ws';

export type DeviceType = 'laptop' | 'phone';
export type CommandType = 'LOCK' | 'SLEEP' | 'RESTART' | 'SHUTDOWN';

// In-memory representation of an authenticated device session
export interface DeviceSession {
  deviceId: string;
  deviceType: DeviceType;
  socket: WebSocket;
  token: string;
  pairedPeerId?: string;
  authenticatedAt: Date;
}

// ---------------- WebSocket Protocol Messages ----------------

// 1. Authentication
export interface AuthMessage {
  type: 'auth';
  deviceId: string;
  deviceType: DeviceType;
  token: string;
}

export interface AuthResultMessage {
  type: 'auth_result';
  success: boolean;
  message: string;
  deviceId?: string;
}

// 2. Commands (Phone -> Server -> Laptop)
export interface CommandMessage {
  type: 'command';
  requestId: string;
  command: CommandType;
}

// 3. Command Results (Laptop -> Server -> Phone)
export interface CommandResultMessage {
  type: 'command_result';
  requestId: string;
  command: CommandType;
  success: boolean;
  error?: string;
}

// 4. Laptop Status Updates (Server -> Phone)
export interface StatusMessage {
  type: 'status';
  status: 'online' | 'offline';
  laptopId?: string;
  timestamp: string;
}

// 5. Heartbeat & Error
export interface PingMessage {
  type: 'ping';
}

export interface PongMessage {
  type: 'pong';
}

export interface GenericErrorMessage {
  type: 'error';
  message: string;
}

// 6. Dynamic Pairing
export interface RequestPairingCodeMessage {
  type: 'request_pairing_code';
  deviceId: string;
}

export interface PairingCodeCreatedMessage {
  type: 'pairing_code_created';
  code: string;
  expiresInSeconds: number;
}

export interface PairWithCodeMessage {
  type: 'pair_with_code';
  phoneDeviceId: string;
  code: string;
}

export interface PairingSuccessMessage {
  type: 'pairing_success';
  success: true;
  token: string;
  laptopId: string;
}

export interface PairingFailedMessage {
  type: 'pairing_failed';
  success: false;
  message: string;
}

export interface PairingCompletedMessage {
  type: 'pairing_completed';
  token: string;
  phoneDeviceId: string;
}

// Union of all possible incoming messages
export type IncomingMessage =
  | AuthMessage
  | CommandMessage
  | CommandResultMessage
  | PingMessage
  | PongMessage
  | RequestPairingCodeMessage
  | PairWithCodeMessage;

// Union of all possible outgoing messages
export type OutgoingMessage =
  | AuthResultMessage
  | CommandMessage
  | CommandResultMessage
  | StatusMessage
  | PongMessage
  | GenericErrorMessage
  | PairingCodeCreatedMessage
  | PairingSuccessMessage
  | PairingFailedMessage
  | PairingCompletedMessage;
