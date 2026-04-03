import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Dialog } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { DatePicker } from 'primeng/datepicker';
import { Message } from 'primeng/message';
import { ImportService, ImportSocialPostRequest } from '../services/import.service';

interface PlatformOption {
  readonly label: string;
  readonly value: string;
}

@Component({
  selector: 'app-import-post-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, Dialog, ButtonModule, InputText, Select, Textarea, DatePicker, Message],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      header="Import Existing Social Post"
      [(visible)]="visible"
      [modal]="true"
      [style]="{ width: '500px' }"
      [closable]="true"
      (onHide)="reset()"
    >
      <div class="import-form">
        @if (errorMsg()) {
          <p-message severity="error" [text]="errorMsg()" styleClass="mb-3 w-full" />
        }
        @if (successMsg()) {
          <p-message severity="success" [text]="successMsg()" styleClass="mb-3 w-full" />
        }

        <div class="field">
          <label for="platform">Platform</label>
          <p-select
            id="platform"
            [options]="platforms"
            [(ngModel)]="platform"
            optionLabel="label"
            optionValue="value"
            placeholder="Select platform"
            styleClass="w-full"
          />
        </div>

        <div class="field">
          <label for="postId">Platform Post ID</label>
          <input pInputText id="postId" [(ngModel)]="platformPostId" placeholder="e.g. 1234567890" class="w-full" />
          <small class="text-color-secondary">The unique post ID from the platform</small>
        </div>

        <div class="field">
          <label for="postUrl">Post URL (optional)</label>
          <input pInputText id="postUrl" [(ngModel)]="postUrl" placeholder="https://..." class="w-full" />
        </div>

        <div class="field">
          <label for="title">Title (optional)</label>
          <input pInputText id="title" [(ngModel)]="title" placeholder="Post title or summary" class="w-full" />
        </div>

        <div class="field">
          <label for="body">Body (optional)</label>
          <textarea pTextarea id="body" [(ngModel)]="body" [rows]="3" placeholder="Post content" class="w-full"></textarea>
        </div>

        <div class="field">
          <label for="publishedAt">Published Date (optional)</label>
          <p-datepicker id="publishedAt" [(ngModel)]="publishedAt" [showTime]="true" dateFormat="yy-mm-dd" styleClass="w-full" />
        </div>
      </div>

      <ng-template #footer>
        <p-button label="Cancel" [text]="true" (onClick)="visible = false" />
        <p-button
          label="Import"
          icon="pi pi-download"
          (onClick)="onImport()"
          [loading]="importing()"
          [disabled]="!platform || !platformPostId"
        />
      </ng-template>
    </p-dialog>
  `,
  styles: `
    .import-form {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
    }
    .field label {
      font-size: 0.8rem;
      font-weight: 600;
      color: var(--p-text-muted-color, #71717a);
    }
    .field small {
      font-size: 0.72rem;
    }
  `,
})
export class ImportPostDialogComponent {
  private readonly importService = inject(ImportService);

  readonly imported = output<void>();

  visible = false;
  platform = '';
  platformPostId = '';
  postUrl = '';
  title = '';
  body = '';
  publishedAt: Date | null = null;

  readonly importing = signal(false);
  readonly errorMsg = signal('');
  readonly successMsg = signal('');

  readonly platforms: PlatformOption[] = [
    { label: 'Twitter / X', value: 'TwitterX' },
    { label: 'LinkedIn', value: 'LinkedIn' },
    { label: 'Instagram', value: 'Instagram' },
    { label: 'YouTube', value: 'YouTube' },
    { label: 'Reddit', value: 'Reddit' },
  ];

  open() {
    this.reset();
    this.visible = true;
  }

  reset() {
    this.platform = '';
    this.platformPostId = '';
    this.postUrl = '';
    this.title = '';
    this.body = '';
    this.publishedAt = null;
    this.errorMsg.set('');
    this.successMsg.set('');
    this.importing.set(false);
  }

  onImport() {
    this.errorMsg.set('');
    this.successMsg.set('');
    this.importing.set(true);

    const request: ImportSocialPostRequest = {
      platform: this.platform,
      platformPostId: this.platformPostId,
      ...(this.postUrl ? { postUrl: this.postUrl } : {}),
      ...(this.title ? { title: this.title } : {}),
      ...(this.body ? { body: this.body } : {}),
      ...(this.publishedAt ? { publishedAt: this.publishedAt.toISOString() } : {}),
    };

    this.importService.importSocialPost(request).subscribe({
      next: () => {
        this.successMsg.set('Post imported successfully! Engagement data will be fetched shortly.');
        this.importing.set(false);
        this.imported.emit();
      },
      error: (err) => {
        this.errorMsg.set(err?.error?.detail ?? 'Failed to import post.');
        this.importing.set(false);
      },
    });
  }
}
