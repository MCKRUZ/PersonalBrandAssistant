import {
  Component,
  OnInit,
  DestroyRef,
  inject,
  signal,
  computed,
  input,
  ViewChild,
  ElementRef,
  AfterViewChecked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TextareaModule } from 'primeng/textarea';
import { MarkdownComponent as MarkdownRenderer } from 'ngx-markdown';
import { ContentEditorStore } from '../../stores/content-editor.store';
import { SignalRService } from '../../services/signalr.service';

@Component({
  selector: 'app-sidecar-chat',
  standalone: true,
  imports: [
    FormsModule,
    ButtonModule,
    TextareaModule,
    MarkdownRenderer,
  ],
  template: `
    <div class="chat-container">
      <div class="chat-header">
        <span class="chat-title">&#10022; Assistant</span>
      </div>

      <div class="messages-list" data-testid="messages-list">
        @for (msg of store.chatMessages(); track msg.timestamp) {
          <div class="message" [class.user-message]="msg.role === 'user'"
            [class.assistant-message]="msg.role === 'assistant'">
            @if (msg.role === 'user') {
              <div class="message-bubble user-bubble">{{ msg.content }}</div>
            } @else {
              <div class="message-bubble assistant-bubble">
                <markdown [data]="msg.content" />
              </div>
              <div class="message-actions">
                <button class="action-btn" data-testid="apply-btn"
                  (click)="applyToEditor(msg.content)"
                  title="Apply to draft">
                  <i class="pi pi-check"></i> Apply
                </button>
                <button class="action-btn" data-testid="copy-btn"
                  (click)="copyToClipboard(msg.content)"
                  title="Copy">
                  <i class="pi pi-copy"></i> Copy
                </button>
              </div>
            }
          </div>
        }

        @if (store.isStreaming() || store.currentTokens()) {
          <div class="message assistant-message" data-testid="streaming-area">
            @if (!store.currentTokens()) {
              <div class="thinking-dots" data-testid="thinking-dots" aria-label="Thinking">
                <span class="dot"></span>
                <span class="dot"></span>
                <span class="dot"></span>
              </div>
            } @else {
              <div class="message-bubble assistant-bubble">
                <markdown [data]="store.currentTokens()" />
              </div>
            }
            <button class="stop-btn" data-testid="stop-btn" (click)="stopGeneration()">
              <i class="pi pi-stop-circle"></i> Stop
            </button>
          </div>
        }

        <div #messagesEnd></div>
      </div>

      <div class="quick-actions">
        @for (chip of quickActionChips(); track chip.label) {
          <button class="quick-chip" data-testid="quick-chip"
            [disabled]="store.isStreaming()"
            (click)="onChipClick(chip.action)">
            {{ chip.label }}
          </button>
        }
      </div>

      <div class="input-area">
        <textarea pTextarea
          [autoResize]="true"
          [rows]="1"
          [(ngModel)]="inputMessage"
          (keydown)="onKeydown($event)"
          [disabled]="store.isStreaming()"
          placeholder="Ask the assistant..."
          data-testid="chat-input"
          class="chat-textarea">
        </textarea>
        <p-button icon="pi pi-send" [rounded]="true" [text]="true"
          [disabled]="!inputMessage() || store.isStreaming()"
          (onClick)="sendMessage()"
          data-testid="send-btn" />
      </div>
    </div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; flex: 1; min-height: 0; }
    .chat-container {
      display: flex;
      flex-direction: column;
      flex: 1;
      min-height: 0;
      background: var(--surface-inset);
    }
    .chat-header {
      padding: 10px 12px;
      border-bottom: 1px solid var(--surface-border);
      flex-shrink: 0;
    }
    .chat-title {
      font-size: 12.5px;
      color: var(--brand-primary);
      font-weight: 600;
    }
    .messages-list {
      flex: 1;
      overflow-y: auto;
      padding: 10px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .message { display: flex; flex-direction: column; }
    .user-message { align-items: flex-end; }
    .assistant-message { align-items: flex-start; }
    .message-bubble {
      max-width: 90%;
      padding: 8px 12px;
      border-radius: var(--r-inner);
      font-size: 14px;
      line-height: 1.5;
    }
    .user-bubble {
      background: var(--brand-primary);
      color: #1a0f0a;
      border-bottom-right-radius: 4px;
    }
    .assistant-bubble {
      background: var(--surface-elevated);
      color: var(--text-primary);
      border: 1px solid var(--surface-border);
      border-bottom-left-radius: 4px;
    }
    .message-actions { display: flex; gap: 4px; margin-top: 4px; }
    .action-btn {
      background: none;
      border: none;
      color: var(--text-secondary);
      font-size: 12px;
      cursor: pointer;
      padding: 2px 6px;
      border-radius: var(--r-control);
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .action-btn:hover { color: var(--text-primary); background: var(--surface-elevated); }

    .thinking-dots {
      display: flex;
      gap: 5px;
      padding: 10px 12px;
      align-items: center;
    }
    .thinking-dots .dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: var(--text-muted);
      animation: blink 1.2s infinite ease-in-out;
    }
    .thinking-dots .dot:nth-child(2) { animation-delay: 0.2s; }
    .thinking-dots .dot:nth-child(3) { animation-delay: 0.4s; }
    @keyframes blink {
      0%, 80%, 100% { opacity: 0.25; }
      40% { opacity: 1; }
    }

    .stop-btn {
      background: none;
      border: 1px solid var(--surface-border);
      color: var(--voice-low);
      font-size: 12px;
      cursor: pointer;
      padding: 4px 8px;
      border-radius: var(--r-control);
      margin-top: 4px;
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .stop-btn:hover { background: var(--surface-elevated); }
    .quick-actions {
      display: flex;
      gap: 6px;
      padding: 8px 10px;
      flex-wrap: wrap;
      border-top: 1px solid var(--surface-border);
      flex-shrink: 0;
    }
    .quick-chip {
      background: transparent;
      border: 1px solid var(--surface-border);
      color: var(--brand-primary);
      font-size: 12px;
      padding: 4px 10px;
      border-radius: var(--r-pill);
      cursor: pointer;
    }
    .quick-chip:hover:not(:disabled) { background: var(--accent-soft); border-color: var(--brand-primary); }
    .quick-chip:disabled { opacity: 0.5; cursor: not-allowed; }
    .input-area {
      display: flex;
      align-items: flex-end;
      gap: 8px;
      padding: 10px;
      border-top: 1px solid var(--surface-border);
      flex-shrink: 0;
    }
    .chat-textarea {
      flex: 1;
      background: var(--surface-card) !important;
      border-color: var(--surface-border) !important;
      color: var(--text-primary) !important;
      font-size: 14px;
      resize: none;
    }
  `],
})
export class SidecarChatComponent implements OnInit, AfterViewChecked {
  readonly contentId = input<string>('');

  readonly store = inject(ContentEditorStore);
  private readonly signalRService = inject(SignalRService);
  private readonly destroyRef = inject(DestroyRef);

  readonly inputMessage = signal('');
  private shouldScroll = false;

  @ViewChild('messagesEnd') messagesEnd!: ElementRef;

  readonly quickActionChips = computed(() => {
    const content = this.store.content();
    if (!content?.body) {
      return [
        { label: 'Draft from idea', action: 'Draft content based on the idea context' },
        { label: 'Draft from scratch', action: 'Draft this content from scratch' },
      ];
    }
    return [
      { label: 'Refine', action: 'Refine and improve this content' },
      { label: 'Shorten', action: 'Shorten this content for the platform' },
      { label: 'Expand', action: 'Expand this content with more detail' },
      { label: 'Change tone', action: 'Rewrite in a more conversational tone' },
    ];
  });

  ngOnInit(): void {
    this.signalRService.connect().catch(() => {
      this.store.addChatMessage('');
      this.store.completeGeneration('Error: failed to connect to AI service. Please refresh.');
    });

    this.signalRService.tokens$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((token) => this.store.appendToken(token));

    this.signalRService.generationComplete$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((fullText) => {
        this.store.completeGeneration(fullText);
        this.shouldScroll = true;
      });

    this.signalRService.generationError$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.store.completeGeneration(this.store.currentTokens() || 'Error: generation failed');
      });

    this.destroyRef.onDestroy(() => {
      this.signalRService.disconnect();
    });
  }

  ngAfterViewChecked(): void {
    if (this.shouldScroll && this.messagesEnd) {
      this.messagesEnd.nativeElement.scrollIntoView({ behavior: 'smooth' });
      this.shouldScroll = false;
    }
  }

  sendMessage(): void {
    const text = this.inputMessage().trim();
    if (!text || this.store.isStreaming()) return;
    const contentId = this.contentId();
    if (!contentId) return;

    this.store.addChatMessage(text);
    this.signalRService.sendChatMessage(contentId, text).catch(() => {
      this.store.completeGeneration('Error: failed to send message. Please try again.');
    });
    this.inputMessage.set('');
    this.shouldScroll = true;
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  applyToEditor(text: string): void {
    this.store.applyToEditor(text);
  }

  copyToClipboard(text: string): void {
    navigator.clipboard.writeText(text);
  }

  async stopGeneration(): Promise<void> {
    const partial = this.store.currentTokens();
    await this.signalRService.disconnect();
    if (partial) {
      this.store.completeGeneration(partial);
    }
    await this.signalRService.connect();
  }

  onChipClick(action: string): void {
    this.inputMessage.set(action);
    this.sendMessage();
  }
}
