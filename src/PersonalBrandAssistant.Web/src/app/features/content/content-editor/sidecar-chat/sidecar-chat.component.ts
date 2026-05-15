import {
  Component,
  OnInit,
  DestroyRef,
  inject,
  signal,
  computed,
  input,
  output,
  ViewChild,
  ElementRef,
  AfterViewChecked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { DrawerModule } from 'primeng/drawer';
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
    DrawerModule,
    ButtonModule,
    TextareaModule,
    MarkdownRenderer,
  ],
  template: `
    <p-drawer
      [visible]="visible()"
      (visibleChange)="visibleChange.emit($event)"
      position="right"
      [modal]="false"
      [dismissible]="false"
      header="AI Chat"
      [style]="{ width: '380px' }"
      styleClass="chat-drawer">

      <div class="chat-container">
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
                    title="Apply to Editor">
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
                <div class="skeleton-shimmer" data-testid="skeleton-shimmer">
                  <div class="skeleton-line"></div>
                  <div class="skeleton-line"></div>
                  <div class="skeleton-line"></div>
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
            placeholder="Ask the AI..."
            data-testid="chat-input"
            class="chat-textarea">
          </textarea>
          <p-button icon="pi pi-send" [rounded]="true" [text]="true"
            [disabled]="!inputMessage() || store.isStreaming()"
            (onClick)="sendMessage()"
            data-testid="send-btn" />
        </div>
      </div>
    </p-drawer>
  `,
  styles: [`
    .chat-container {
      display: flex;
      flex-direction: column;
      height: 100%;
    }
    .messages-list {
      flex: 1;
      overflow-y: auto;
      padding: 8px;
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
      border-radius: 12px;
      font-size: 14px;
      line-height: 1.5;
    }
    .user-bubble {
      background: #1f6feb;
      color: #fff;
      border-bottom-right-radius: 4px;
    }
    .assistant-bubble {
      background: #161b22;
      color: #e6edf3;
      border-bottom-left-radius: 4px;
    }
    .message-actions {
      display: flex;
      gap: 4px;
      margin-top: 4px;
    }
    .action-btn {
      background: none;
      border: none;
      color: #8b949e;
      font-size: 12px;
      cursor: pointer;
      padding: 2px 6px;
      border-radius: 4px;
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .action-btn:hover { color: #f0f6fc; background: #21262d; }
    .skeleton-shimmer { padding: 8px 12px; }
    .skeleton-line {
      height: 14px;
      background: linear-gradient(90deg, #21262d 25%, #30363d 50%, #21262d 75%);
      background-size: 200% 100%;
      animation: shimmer 1.5s infinite;
      border-radius: 4px;
      margin-bottom: 8px;
    }
    .skeleton-line:nth-child(2) { width: 85%; }
    .skeleton-line:nth-child(3) { width: 70%; }
    @keyframes shimmer {
      0% { background-position: 200% 0; }
      100% { background-position: -200% 0; }
    }
    .stop-btn {
      background: none;
      border: 1px solid #f8514980;
      color: #f85149;
      font-size: 12px;
      cursor: pointer;
      padding: 4px 8px;
      border-radius: 6px;
      margin-top: 4px;
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .stop-btn:hover { background: #f8514920; }
    .quick-actions {
      display: flex;
      gap: 6px;
      padding: 8px;
      flex-wrap: wrap;
      border-top: 1px solid #21262d;
    }
    .quick-chip {
      background: transparent;
      border: 1px solid #30363d;
      color: #58a6ff;
      font-size: 12px;
      padding: 4px 10px;
      border-radius: 16px;
      cursor: pointer;
    }
    .quick-chip:hover:not(:disabled) { background: #1f6feb20; border-color: #58a6ff; }
    .quick-chip:disabled { opacity: 0.5; cursor: not-allowed; }
    .input-area {
      display: flex;
      align-items: flex-end;
      gap: 8px;
      padding: 8px;
      border-top: 1px solid #21262d;
    }
    .chat-textarea {
      flex: 1;
      background: #161b22 !important;
      border-color: #30363d !important;
      color: #f0f6fc !important;
      font-size: 14px;
      resize: none;
    }
    :host ::ng-deep .p-drawer-content { display: flex; flex-direction: column; overflow: hidden; }
  `],
})
export class SidecarChatComponent implements OnInit, AfterViewChecked {
  readonly visible = input<boolean>(false);
  readonly contentId = input<string>('');
  readonly visibleChange = output<boolean>();

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
