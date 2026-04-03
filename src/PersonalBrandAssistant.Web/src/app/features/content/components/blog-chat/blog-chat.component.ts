import { Component, inject, input, output, signal, computed, OnInit, ElementRef, ViewChild, SecurityContext } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ButtonModule } from 'primeng/button';
import { TextareaModule } from 'primeng/textarea';
import { MessageService } from 'primeng/api';
import { BlogChatService } from '../../services/blog-chat.service';
import { ChatMessage, FinalizedDraft } from '../../models/blog-chat.models';
import { marked } from 'marked';

@Component({
  selector: 'app-blog-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, TextareaModule],
  template: `
    <div class="grid">
      <!-- Chat Panel -->
      <div class="col-5">
        <div class="surface-card border-round p-3 h-full flex flex-column">
          <div class="text-lg font-semibold mb-3">Chat</div>

          <div #messageContainer class="message-list flex-1 overflow-y-auto mb-3" style="max-height: 500px;">
            @for (msg of messages(); track $index) {
              <div class="mb-3" [class]="msg.role === 'user' ? 'text-right' : 'text-left'">
                <div class="inline-block p-2 border-round-lg text-sm"
                     style="max-width: 90%;"
                     [class]="msg.role === 'user' ? 'bg-primary text-primary-contrast' : 'surface-ground'">
                  <div class="font-semibold mb-1" style="font-size: 0.75rem;">{{ msg.role === 'user' ? 'You' : 'Claude' }}</div>
                  <div class="white-space-pre-wrap" style="font-size: 0.85rem;">{{ msg.content | slice:0:300 }}{{ msg.content.length > 300 ? '...' : '' }}</div>
                </div>
              </div>
            }

            @if (streaming()) {
              <div class="text-left mb-3">
                <div class="inline-block p-2 border-round-lg surface-ground text-sm" style="max-width: 90%;">
                  <div class="font-semibold mb-1" style="font-size: 0.75rem;">Claude</div>
                  <div class="white-space-pre-wrap" style="font-size: 0.85rem;">{{ streamBuffer() | slice:0:300 }}{{ streamBuffer().length > 300 ? '...' : '' }}</div>
                  <span class="typing-indicator">...</span>
                </div>
              </div>
            }

            @if (error()) {
              <div class="p-2 border-round bg-red-50 text-red-700 mb-3 text-sm">
                {{ error() }}
                <button pButton label="Retry" class="p-button-text p-button-sm ml-2"
                        (click)="error.set(null)"></button>
              </div>
            }
          </div>

          <div class="flex gap-2">
            <textarea pTextarea
              [(ngModel)]="inputMessage"
              [disabled]="streaming()"
              placeholder="Describe your blog post idea..."
              rows="2"
              class="flex-1 text-sm"
              (keydown.enter)="onEnter($event)">
            </textarea>
            <button pButton icon="pi pi-send"
                    [disabled]="streaming() || !inputMessage.trim()"
                    (click)="send()" class="p-button-sm"></button>
          </div>

          <div class="flex justify-content-end mt-2">
            <button pButton label="Finalize Draft" icon="pi pi-check"
                    severity="success" size="small"
                    [disabled]="streaming() || messages().length === 0"
                    [loading]="finalizing()"
                    (click)="finalizeDraft()"></button>
          </div>
        </div>
      </div>

      <!-- Preview Panel -->
      <div class="col-7">
        <div class="surface-card border-round p-3 h-full flex flex-column">
          <div class="flex align-items-center justify-content-between mb-3">
            <div class="text-lg font-semibold">Preview</div>
            @if (previewContent()) {
              <span class="text-xs text-color-secondary">Live from latest response</span>
            }
          </div>

          <div class="flex-1 overflow-y-auto" style="max-height: 560px;">
            @if (previewHtml()) {
              <div class="blog-preview prose" [innerHTML]="previewHtml()"></div>
            } @else {
              <div class="flex align-items-center justify-content-center h-full text-color-secondary">
                <div class="text-center">
                  <i class="pi pi-eye text-4xl mb-3 block"></i>
                  <div>Blog preview will appear here</div>
                  <div class="text-sm mt-1">Start chatting to generate content</div>
                </div>
              </div>
            }
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .typing-indicator {
      animation: blink 1.4s infinite;
      font-weight: bold;
    }
    @keyframes blink {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.3; }
    }
    :host ::ng-deep .blog-preview {
      font-family: 'Georgia', serif;
      line-height: 1.7;
      color: var(--text-color);

      h1 { font-size: 1.8rem; font-weight: 700; margin: 0 0 1rem 0; line-height: 1.2; }
      h2 { font-size: 1.3rem; font-weight: 600; margin: 1.5rem 0 0.75rem 0; }
      h3 { font-size: 1.1rem; font-weight: 600; margin: 1.2rem 0 0.5rem 0; }
      p { margin: 0 0 1rem 0; }
      code { background: var(--surface-ground); padding: 0.15rem 0.4rem; border-radius: 4px; font-size: 0.85em; }
      pre { background: var(--surface-ground); padding: 1rem; border-radius: 8px; overflow-x: auto; margin: 1rem 0; }
      pre code { background: none; padding: 0; }
      blockquote { border-left: 3px solid var(--primary-color); margin: 1rem 0; padding: 0.5rem 1rem; color: var(--text-color-secondary); }
      ul, ol { margin: 0 0 1rem 0; padding-left: 1.5rem; }
      li { margin-bottom: 0.3rem; }
    }
  `]
})
export class BlogChatComponent implements OnInit {
  contentId = input.required<string>();
  finalized = output<FinalizedDraft>();

  @ViewChild('messageContainer') messageContainer!: ElementRef;

  private readonly chatService = inject(BlogChatService);
  private readonly messageService = inject(MessageService);
  private readonly sanitizer = inject(DomSanitizer);

  messages = signal<ChatMessage[]>([]);
  streaming = signal(false);
  streamBuffer = signal('');
  finalizing = signal(false);
  error = signal<string | null>(null);
  inputMessage = '';

  previewContent = computed(() => {
    // Show stream buffer while streaming, otherwise latest assistant message
    if (this.streaming() && this.streamBuffer()) {
      return this.streamBuffer();
    }
    const msgs = this.messages();
    for (let i = msgs.length - 1; i >= 0; i--) {
      if (msgs[i].role === 'assistant') return msgs[i].content;
    }
    return '';
  });

  previewHtml = computed(() => {
    const md = this.previewContent();
    if (!md) return null;
    const raw = marked.parse(md, { async: false }) as string;
    return this.sanitizer.sanitize(SecurityContext.HTML, raw);
  });

  ngOnInit(): void {
    this.chatService.getHistory(this.contentId()).subscribe({
      next: (history) => this.messages.set(history),
      error: () => {} // No history yet is fine
    });
  }

  send(): void {
    const message = this.inputMessage.trim();
    if (!message || this.streaming()) return;

    this.inputMessage = '';
    this.error.set(null);

    this.messages.update(msgs => [...msgs, {
      role: 'user',
      content: message,
      timestamp: new Date().toISOString()
    }]);

    this.streaming.set(true);
    this.streamBuffer.set('');
    this.scrollToBottom();

    this.chatService.sendMessage(this.contentId(), message).subscribe({
      next: (chunk) => {
        this.streamBuffer.update(buf => buf + chunk);
        this.scrollToBottom();
      },
      error: (err) => {
        this.streaming.set(false);
        this.error.set(err?.message ?? 'Stream failed. Please try again.');
      },
      complete: () => {
        const content = this.streamBuffer();
        if (content) {
          this.messages.update(msgs => [...msgs, {
            role: 'assistant',
            content,
            timestamp: new Date().toISOString()
          }]);
        }
        this.streaming.set(false);
        this.streamBuffer.set('');
        this.scrollToBottom();
      }
    });
  }

  finalizeDraft(): void {
    this.finalizing.set(true);
    this.chatService.finalize(this.contentId()).subscribe({
      next: (draft) => {
        this.finalizing.set(false);
        this.finalized.emit(draft);
        this.messageService.add({
          severity: 'success',
          summary: 'Draft finalized',
          detail: `"${draft.title}" is ready for Substack prep.`
        });
      },
      error: (err) => {
        this.finalizing.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Finalization failed',
          detail: err?.error?.error ?? 'Could not finalize draft.'
        });
      }
    });
  }

  onEnter(event: Event): void {
    const keyEvent = event as KeyboardEvent;
    if (!keyEvent.shiftKey) {
      keyEvent.preventDefault();
      this.send();
    }
  }

  private scrollToBottom(): void {
    setTimeout(() => {
      const el = this.messageContainer?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }
}
