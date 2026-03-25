import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, tap } from 'rxjs';
import {
  ConnectionStatus,
  TimelineEntry,
  SidecarServerMessage,
} from '../../../shared/models';
import { SidecarWebSocketService } from '../services/sidecar-websocket.service';

interface SidecarState {
  readonly timeline: readonly TimelineEntry[];
  readonly connectionStatus: ConnectionStatus;
  readonly isRunning: boolean;
  readonly activeSessionId: string | null;
  readonly lastError: string | null;
}

const initialState: SidecarState = {
  timeline: [],
  connectionStatus: 'disconnected',
  isRunning: false,
  activeSessionId: null,
  lastError: null,
};

export const SidecarStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => ({
    canSend: computed(() => store.connectionStatus() === 'connected' && !store.isRunning()),
  })),
  withMethods((store, wsService = inject(SidecarWebSocketService)) => ({
    connect(): void {
      wsService.connect();
    },

    disconnect(): void {
      wsService.disconnect();
    },

    sendMessage(text: string): void {
      const trimmed = text.trim();
      if (!trimmed) return;

      const userEntry: TimelineEntry = {
        kind: 'user',
        text: trimmed,
        timestamp: new Date().toISOString(),
      };
      patchState(store, {
        timeline: [...store.timeline(), userEntry],
        lastError: null,
      });
      const sid = store.activeSessionId();
      wsService.send({
        type: 'send-message',
        payload: sid ? { message: trimmed, sessionId: sid } : { message: trimmed },
      });
    },

    newSession(): void {
      patchState(store, { timeline: [], activeSessionId: null, lastError: null });
      wsService.send({ type: 'new-session' });
    },

    abort(): void {
      wsService.send({ type: 'abort' });
    },

    syncConnectionStatus(): void {
      patchState(store, { connectionStatus: wsService.connectionStatus() });
    },

    listenToMessages: rxMethod<SidecarServerMessage>(
      pipe(
        tap((msg) => {
          switch (msg.type) {
            case 'chat-event': {
              // Suppress background LLM task output (scoring JSON, etc.)
              // that bleeds through the shared sidecar broadcast bus.
              const c = msg.payload.content?.trim() ?? '';
              const isBackgroundJson = c.startsWith('[{') && c.includes('"score"');
              if (!isBackgroundJson) {
                patchState(store, {
                  timeline: [...store.timeline(), { kind: 'event', event: msg.payload }],
                });
              }
              break;
            }
            case 'status':
              patchState(store, {
                isRunning: msg.payload.isRunning,
                activeSessionId: msg.payload.sessionId,
              });
              break;
            case 'config':
              patchState(store, { activeSessionId: msg.payload.sessionId });
              break;
            case 'session-update':
              patchState(store, { activeSessionId: msg.payload.sessionId });
              break;
            case 'error':
              patchState(store, { lastError: msg.payload.message });
              break;
            case 'file-change':
              // file changes are informational, no state update needed
              break;
          }
        })
      )
    ),
  }))
);
