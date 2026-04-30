import {
  Component,
  OnInit,
  OnDestroy,
  ViewChild,
  ElementRef,
  effect,
  inject,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Textarea } from 'primeng/textarea';
import { Button } from 'primeng/button';
import { StreamingTextComponent } from '../../shared/components/streaming-text/streaming-text.component';
import { QuickPromptChipComponent } from '../../shared/components/quick-prompt-chip/quick-prompt-chip.component';
import { SidecarStore, SidecarMessage } from './sidecar.store';

@Component({
  selector: 'app-sidecar',
  standalone: true,
  imports: [
    FormsModule,
    DatePipe,
    Textarea,
    Button,
    StreamingTextComponent,
    QuickPromptChipComponent,
  ],
  templateUrl: './sidecar.component.html',
  styleUrl: './sidecar.component.scss',
})
export class SidecarComponent implements OnInit, OnDestroy {
  readonly store = inject(SidecarStore);
  composerText = '';
  private userScrolledUp = false;

  @ViewChild('conversationArea') private conversationArea!: ElementRef<HTMLDivElement>;

  constructor() {
    effect(() => {
      this.store.messages();
      this.store.partialText();
      if (!this.userScrolledUp) {
        this.scrollToBottom();
      }
    });
  }

  ngOnInit(): void {
    this.store.initRouteTracking();
    this.store.connect();
  }

  ngOnDestroy(): void {
    this.store.disconnect();
  }

  onScroll(event: Event): void {
    const el = event.target as HTMLElement;
    const threshold = 100;
    this.userScrolledUp = el.scrollHeight - el.scrollTop - el.clientHeight > threshold;
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  send(): void {
    const text = this.composerText.trim();
    if (!text) return;
    this.composerText = '';
    this.store.sendMessage(text);
  }

  onQuickPrompt(prompt: string): void {
    this.store.sendMessage(prompt);
  }

  isEditorRoute(): boolean {
    return this.store.routeContext() === 'content-editor';
  }

  trackMessage(_index: number, msg: SidecarMessage): string {
    return msg.id;
  }

  private scrollToBottom(): void {
    requestAnimationFrame(() => {
      if (this.conversationArea?.nativeElement) {
        const el = this.conversationArea.nativeElement;
        el.scrollTop = el.scrollHeight;
      }
    });
  }
}
