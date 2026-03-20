import { Injectable, OnDestroy, signal } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { ConnectionStatus, SidecarClientMessage, SidecarServerMessage } from '../../../shared/models';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class SidecarWebSocketService implements OnDestroy {
  private ws: WebSocket | null = null;
  private readonly _messages = new Subject<SidecarServerMessage>();
  private readonly _connectionStatus = signal<ConnectionStatus>('disconnected');
  private reconnectAttempts = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private intentionalDisconnect = false;

  readonly connectionStatus = this._connectionStatus.asReadonly();
  readonly messages$: Observable<SidecarServerMessage> = this._messages.asObservable();

  connect(): void {
    if (this.ws?.readyState === WebSocket.OPEN || this.ws?.readyState === WebSocket.CONNECTING) {
      return;
    }

    this.intentionalDisconnect = false;
    this._connectionStatus.set('connecting');

    const wsUrl = environment.sidecarUrl.replace(/^http/, 'ws') + '/ws';
    this.ws = new WebSocket(wsUrl);

    this.ws.onopen = () => {
      this._connectionStatus.set('connected');
      this.reconnectAttempts = 0;
    };

    this.ws.onmessage = (event: MessageEvent) => {
      try {
        const msg = JSON.parse(event.data) as SidecarServerMessage;
        this._messages.next(msg);
      } catch {
        // ignore malformed messages
      }
    };

    this.ws.onclose = () => {
      this._connectionStatus.set('disconnected');
      this.ws = null;
      if (!this.intentionalDisconnect) {
        this.scheduleReconnect();
      }
    };

    this.ws.onerror = () => {
      // onclose will fire after onerror
    };
  }

  disconnect(): void {
    this.intentionalDisconnect = true;
    this.clearReconnect();
    this.ws?.close();
    this.ws = null;
    this._connectionStatus.set('disconnected');
  }

  send(msg: SidecarClientMessage): void {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }

  ngOnDestroy(): void {
    this.disconnect();
    this._messages.complete();
  }

  private scheduleReconnect(): void {
    this.clearReconnect();
    const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 30_000);
    this.reconnectAttempts++;
    this.reconnectTimer = setTimeout(() => this.connect(), delay);
  }

  private clearReconnect(): void {
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }
}
