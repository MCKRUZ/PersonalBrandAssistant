import { Component, inject, input, output, signal, OnInit, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextarea } from 'primeng/inputtextarea';
import { Card } from 'primeng/card';
import { ProgressSpinner } from 'primeng/progressspinner';
import { MessageService } from 'primeng/api';
import { BlogChatService } from '../../services/blog-chat.service';
import { ChatMessage, FinalizedDraft } from '../../models/blog-chat.models';

@Component({
  selector: 'app-blog-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, InputTextarea, Card, ProgressSpinner],
  template: `
    <p-card header="Blog Authoring Chat">
      <div #messageContainer class="message-list" style="height: 400px; overflow-y: auto; padding: 1rem;">
        @for (msg of messages(); track $index) {
          <div class="mb-3" [class]="msg.role === 'user' ? 'text-right' : 'text-left'">
            <div class="inline-block p-3 border-round-lg max-w-30rem"
                 [class]="msg.role === 'user' ? 'bg-primary text-primary-contrast' : 'surface-ground'">
              <div class="text-sm font-semibold mb-1">{{ msg.role === 'user' ? 'You' : 'Claude' }}</div>
              <div class="white-space-pre-wrap">{{ msg.content }}</div>
            </div>
          </div>
        }

        @if (streaming()) {
          <div class="text-left mb-3">
            <div class="inline-block p-3 border-round-lg surface-ground">
              <div class="text-sm font-semibold mb-1">Claude</div>
              <div class="white-space-pre-wrap">{{ streamBuffer() }}</div>
              <span class="typing-indicator">...</span>
            </div>
          </div>
        }

        @if (error()) {
          <div class="p-3 border-round bg-red-50 text-red-700 mb-3">
            {{ error() }}
            <button pButton label="Retry" class="p-button-text p-button-sm ml-2"
                    (click)="error.set(null)"></button>
          </div>
        }
      </div>

      <div class="flex gap-2 mt-3">
        <textarea pInputTextarea
          [(ngModel)]="inputMessage"
          [disabled]="streaming()"
          placeholder="Describe your blog post idea..."
          rows="2"
          class="flex-1"
          (keydown.enter)="onEnter($event)">
        </textarea>
        <button pButton label="Send" icon="pi pi-send"
                [disabled]="streaming() || !inputMessage.trim()"
                (click)="send()"></button>
      </div>

      <div class="flex justify-content-end mt-3">
        <button pButton label="Finalize Draft" icon="pi pi-check"
                severity="success"
                [disabled]="streaming() || messages().length === 0"
                [loading]="finalizing()"
                (click)="finalizeDraft()"></button>
      </div>
    </p-card>
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
  `]
})
export class BlogChatComponent implements OnInit {
  contentId = input.required<string>();
  finalized = output<FinalizedDraft>();

  @ViewChild('messageContainer') messageContainer!: ElementRef;

  private readonly chatService = inject(BlogChatService);
  private readonly messageService = inject(MessageService);

  messages = signal<ChatMessage[]>([]);
  streaming = signal(false);
  streamBuffer = signal('');
  finalizing = signal(false);
  error = signal<string | null>(null);
  inputMessage = '';

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
