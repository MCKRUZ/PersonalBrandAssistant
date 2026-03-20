import { Component, inject, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Dialog } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';
import { ButtonModule } from 'primeng/button';
import { MessageService } from 'primeng/api';
import { PlatformService } from '../services/platform.service';
import { PlatformType } from '../../../shared/models';

@Component({
  selector: 'app-test-post-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, Dialog, TextareaModule, ButtonModule],
  template: `
    <p-dialog header="Test Post" [(visible)]="visible" [modal]="true" [style]="{ width: '400px' }">
      <div class="flex flex-column gap-3">
        <div>
          <label class="block font-semibold mb-1">Message (optional)</label>
          <textarea pTextarea [(ngModel)]="message" class="w-full" [rows]="4" placeholder="Test message..."></textarea>
        </div>
        <div class="flex justify-content-end gap-2">
          <p-button label="Cancel" severity="secondary" (onClick)="visible = false" />
          <p-button label="Send Test" icon="pi pi-send" (onClick)="send()" [loading]="sending" />
        </div>
      </div>
    </p-dialog>
  `,
})
export class TestPostDialogComponent {
  private readonly platformService = inject(PlatformService);
  private readonly messageService = inject(MessageService);

  sent = output<void>();

  visible = false;
  sending = false;
  message = '';
  platformType?: PlatformType;

  open(type: PlatformType) {
    this.platformType = type;
    this.message = '';
    this.visible = true;
  }

  send() {
    if (!this.platformType) return;
    this.sending = true;
    this.platformService.testPost(this.platformType, { confirm: true, message: this.message || undefined }).subscribe({
      next: () => {
        this.sending = false;
        this.visible = false;
        this.messageService.add({ severity: 'success', summary: 'Sent', detail: 'Test post sent!' });
        this.sent.emit();
      },
      error: () => { this.sending = false; },
    });
  }
}
