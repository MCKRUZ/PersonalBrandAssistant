export type ChatEventType =
  | 'thinking'
  | 'file-edit'
  | 'file-read'
  | 'bash-command'
  | 'bash-output'
  | 'tool-use'
  | 'tool-result'
  | 'summary'
  | 'error'
  | 'status';

export interface ChatEvent {
  readonly id: string;
  readonly type: ChatEventType;
  readonly content: string;
  readonly timestamp: string;
  readonly metadata?: Record<string, unknown>;
}

export type ConnectionStatus = 'connected' | 'disconnected' | 'connecting';

export interface SidecarConfig {
  readonly sessionId: string;
  readonly model?: string;
  readonly version?: string;
}

export interface SidecarSession {
  readonly sessionId: string;
  readonly startedAt: string;
}

export interface SidecarStatusPayload {
  readonly isRunning: boolean;
  readonly sessionId: string | null;
}

export type SidecarServerMessage =
  | { readonly type: 'config'; readonly payload: SidecarConfig }
  | { readonly type: 'chat-event'; readonly payload: ChatEvent }
  | { readonly type: 'status'; readonly payload: SidecarStatusPayload }
  | { readonly type: 'session-update'; readonly payload: SidecarSession }
  | { readonly type: 'file-change'; readonly payload: { readonly path: string; readonly action: string } }
  | { readonly type: 'error'; readonly payload: { readonly message: string } };

export type SidecarClientMessage =
  | { readonly type: 'send-message'; readonly payload: { readonly message: string; readonly sessionId?: string } }
  | { readonly type: 'new-session' }
  | { readonly type: 'abort' };

export type TimelineEntry =
  | { readonly kind: 'user'; readonly text: string; readonly timestamp: string }
  | { readonly kind: 'event'; readonly event: ChatEvent };
