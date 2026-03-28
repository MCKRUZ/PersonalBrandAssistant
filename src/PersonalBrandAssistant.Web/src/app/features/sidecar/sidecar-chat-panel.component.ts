import {
  Component,
  ChangeDetectionStrategy,
  inject,
  OnInit,
  OnDestroy,
  ElementRef,
  viewChild,
  effect,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { Textarea } from 'primeng/textarea';
import { SidecarStore } from './store/sidecar.store';
import { SidecarWebSocketService } from './services/sidecar-websocket.service';
import { ChatEventComponent } from './components/chat-event.component';

@Component({
  selector: 'app-sidecar-chat-panel',
  standalone: true,
  imports: [FormsModule, ButtonModule, Textarea, ChatEventComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sidecar-chat">
      <!-- Header -->
      <div class="chat-header">
        <div class="connection-status">
          <span
            class="status-dot"
            [class.connected]="store.connectionStatus() === 'connected'"
            [class.connecting]="store.connectionStatus() === 'connecting'"
            [class.disconnected]="store.connectionStatus() === 'disconnected'"
          ></span>
          <span class="status-label">{{ store.connectionStatus() }}</span>
        </div>
        <div class="flex-1"></div>
        <p-button
          icon="pi pi-plus"
          label="New"
          [text]="true"
          size="small"
          (onClick)="store.newSession()"
        />
      </div>

      <!-- Error banner -->
      @if (store.lastError()) {
        <div class="error-banner">
          <i class="pi pi-exclamation-circle"></i>
          {{ store.lastError() }}
        </div>
      }

      <!-- Timeline -->
      <div class="chat-timeline" #timeline (scroll)="onScroll()">
        @if (store.timeline().length === 0) {
          <div class="empty-state">
            <i class="pi pi-comments"></i>
            <p>Send a message to start a conversation with Claude</p>
          </div>
        }
        @for (entry of store.timeline(); track $index) {
          @if (entry.kind === 'user') {
            <div class="user-message">
              <div class="user-bubble">{{ entry.text }}</div>
            </div>
          } @else {
            <app-chat-event [event]="entry.event" />
          }
        }
      </div>

      <!-- Input area -->
      <div class="chat-input-area">
        <textarea
          pInputTextarea
          [autoResize]="true"
          rows="1"
          [(ngModel)]="inputText"
          (keydown)="onKeydown($event)"
          placeholder="Message Claude..."
          class="chat-textarea"
          [disabled]="store.connectionStatus() !== 'connected'"
        ></textarea>
        <div class="input-actions">
          @if (store.isRunning()) {
            <p-button
              icon="pi pi-stop-circle"
              severity="danger"
              [text]="true"
              (onClick)="store.abort()"
              pTooltip="Stop"
            />
          } @else {
            <p-button
              icon="pi pi-send"
              [text]="true"
              [disabled]="!store.canSend() || !inputText().trim()"
              (onClick)="send()"
            />
          }
        </div>
      </div>
    </div>
  `,
  styleUrl: './sidecar-chat-panel.component.scss',
})
export class SidecarChatPanelComponent implements OnInit, OnDestroy {
  readonly store = inject(SidecarStore);
  private readonly wsService = inject(SidecarWebSocketService);
  private readonly timelineRef = viewChild<ElementRef<HTMLDivElement>>('timeline');

  readonly inputText = signal('');
  private autoScroll = true;
  private statusInterval: ReturnType<typeof setInterval> | null = null;

  constructor() {
    // Auto-scroll when timeline changes
    effect(() => {
      const _ = this.store.timeline();
      if (this.autoScroll) {
        requestAnimationFrame(() => this.scrollToBottom());
      }
    });
  }

  ngOnInit(): void {
    this.store.connect();
    this.store.listenToMessages(this.wsService.messages$);

    // Poll connection status from the service signal
    this.statusInterval = setInterval(() => this.store.syncConnectionStatus(), 500);
  }

  ngOnDestroy(): void {
    if (this.statusInterval !== null) {
      clearInterval(this.statusInterval);
    }
  }

  send(): void {
    const text = this.inputText().trim();
    if (!text) return;
    this.store.sendMessage(text);
    this.inputText.set('');
    this.autoScroll = true;
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  onScroll(): void {
    const el = this.timelineRef()?.nativeElement;
    if (!el) return;
    const threshold = 50;
    this.autoScroll = el.scrollHeight - el.scrollTop - el.clientHeight < threshold;
  }

  private scrollToBottom(): void {
    const el = this.timelineRef()?.nativeElement;
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  }
}
