import { computed, inject, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, NavigationEnd, ActivatedRoute } from '@angular/router';
import { filter, map } from 'rxjs';
import { signalStore, withState, withMethods, withComputed } from '@ngrx/signals';
import { patchState } from '@ngrx/signals';
import { SidecarSignalRService, ConnectionStatus } from '../../core/services/sidecar-signalr.service';
import { DraftApplyService } from './draft-apply.service';
import { QUICK_PROMPTS } from './quick-prompts.config';

export interface SidecarMessage {
  readonly id: string;
  readonly role: 'user' | 'assistant';
  readonly text: string;
  readonly timestamp: string;
  readonly isDraft?: boolean;
  readonly streamId?: string;
}

interface SidecarState {
  readonly messages: readonly SidecarMessage[];
  readonly isStreaming: boolean;
  readonly currentStreamId: string | null;
  readonly partialText: string;
  readonly routeContext: string;
  readonly routeTitle: string;
  readonly connectionStatus: ConnectionStatus;
}

const MAX_MESSAGES = 100;

const PROMPTS_KEY = 'pba-quick-prompts';

function loadQuickPrompts(context: string): readonly string[] {
  try {
    const raw = localStorage.getItem(PROMPTS_KEY);
    if (raw) {
      const overrides = JSON.parse(raw) as Record<string, string[]>;
      if (overrides[context]) return overrides[context];
    }
  } catch { /* ignore invalid JSON */ }
  return QUICK_PROMPTS[context] ?? [];
}

function generateId(): string {
  return crypto.randomUUID();
}

export const SidecarStore = signalStore(
  { providedIn: 'root' },
  withState<SidecarState>({
    messages: [],
    isStreaming: false,
    currentStreamId: null,
    partialText: '',
    routeContext: 'dashboard',
    routeTitle: 'Dashboard',
    connectionStatus: 'disconnected',
  }),
  withComputed((store) => ({
    canSend: computed(() => store.connectionStatus() === 'connected' && !store.isStreaming()),
    quickPrompts: computed(() => loadQuickPrompts(store.routeContext())),
  })),
  withMethods((store) => {
    const signalr = inject(SidecarSignalRService);
    const draftApply = inject(DraftApplyService);
    const router = inject(Router);
    const route = inject(ActivatedRoute);
    const destroyRef = inject(DestroyRef);

    function appendMessage(msg: SidecarMessage): void {
      const current = store.messages();
      const next = current.length >= MAX_MESSAGES
        ? [...current.slice(-(MAX_MESSAGES - 1)), msg]
        : [...current, msg];
      patchState(store, { messages: next });
    }

    return {
      initRouteTracking(): void {
        router.events.pipe(
          filter((e): e is NavigationEnd => e instanceof NavigationEnd),
          map(() => {
            let r = route;
            while (r.firstChild) r = r.firstChild;
            return {
              context: (r.snapshot.data['sidecarContext'] as string) ?? 'dashboard',
              title: (r.snapshot.data['title'] as string) ?? 'Dashboard',
            };
          }),
          takeUntilDestroyed(destroyRef),
        ).subscribe((data) => {
          patchState(store, { routeContext: data.context, routeTitle: data.title });
        });
      },

      async connect(): Promise<void> {
        signalr.on('ReceiveTokenBatch', (_streamId: unknown, tokens: unknown) => {
          const streamId = _streamId as string;
          const tokenArr = tokens as string[];
          patchState(store, {
            partialText: store.partialText() + tokenArr.join(''),
            isStreaming: true,
            currentStreamId: streamId,
          });
        });

        signalr.on('StreamComplete', (_streamId: unknown, _isDraft: unknown, _fullText: unknown) => {
          const streamId = _streamId as string;
          const isDraft = _isDraft as boolean;
          const fullText = _fullText as string;
          appendMessage({
            id: generateId(),
            role: 'assistant',
            text: fullText,
            timestamp: new Date().toISOString(),
            isDraft: isDraft || undefined,
            streamId: isDraft ? streamId : undefined,
          });
          patchState(store, {
            partialText: '',
            isStreaming: false,
            currentStreamId: null,
          });
        });

        signalr.on('StreamError', (_streamId: unknown, _error: unknown) => {
          const error = _error as string;
          appendMessage({
            id: generateId(),
            role: 'assistant',
            text: `Error: ${error}`,
            timestamp: new Date().toISOString(),
          });
          patchState(store, {
            partialText: '',
            isStreaming: false,
            currentStreamId: null,
          });
        });

        signalr.on('TypingStarted', (_streamId: unknown) => {
          const streamId = _streamId as string;
          patchState(store, { isStreaming: true, currentStreamId: streamId });
        });

        signalr.on('TokenUsage', () => {
          // Token usage tracking - can be extended to display cost info
        });

        await signalr.connect();
        patchState(store, { connectionStatus: signalr.connectionStatus() });
      },

      async disconnect(): Promise<void> {
        await signalr.disconnect();
        patchState(store, { connectionStatus: 'disconnected' });
      },

      syncConnectionStatus(): void {
        patchState(store, { connectionStatus: signalr.connectionStatus() });
      },

      async sendMessage(text: string): Promise<void> {
        if (!store.canSend()) return;
        appendMessage({
          id: generateId(),
          role: 'user',
          text,
          timestamp: new Date().toISOString(),
        });
        await signalr.invoke('SendMessage', store.routeContext(), text);
      },

      async cancelStream(): Promise<void> {
        const streamId = store.currentStreamId();
        if (streamId) {
          await signalr.invoke('CancelStream', streamId);
        }
      },

      async applyDraft(messageId: string): Promise<void> {
        const msg = store.messages().find((m) => m.id === messageId);
        if (!msg?.isDraft || !msg.streamId) return;
        await signalr.invoke('ApplyDraft', msg.streamId);
        draftApply.applyDraft(msg.text);
      },

      discardDraft(messageId: string): void {
        patchState(store, {
          messages: store.messages().filter((m) => m.id !== messageId),
        });
      },

      async regenerate(messageId: string): Promise<void> {
        const msgs = store.messages();
        const idx = msgs.findIndex((m) => m.id === messageId);
        if (idx <= 0) return;
        const prevUser = [...msgs.slice(0, idx)].reverse().find((m) => m.role === 'user');
        if (prevUser) {
          patchState(store, {
            messages: msgs.filter((m) => m.id !== messageId),
          });
          await signalr.invoke('SendMessage', store.routeContext(), prevUser.text);
        }
      },
    };
  }),
);
