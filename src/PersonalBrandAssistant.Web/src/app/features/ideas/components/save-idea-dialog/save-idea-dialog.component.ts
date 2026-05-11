import { Component, inject, input, output, model, OnChanges, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { TextareaModule } from 'primeng/textarea';
import { InputTextModule } from 'primeng/inputtext';
import { ChipModule } from 'primeng/chip';
import { IdeaStore } from '../../store/idea.store';
import { Idea } from '../../../../models/idea.model';

@Component({
  selector: 'app-save-idea-dialog',
  standalone: true,
  imports: [FormsModule, DialogModule, ButtonModule, TextareaModule, InputTextModule, ChipModule],
  template: `
    <p-dialog
      [visible]="visible()"
      (visibleChange)="visible.set($event)"
      [modal]="true"
      header="Save Idea"
      [style]="{ width: '500px' }"
      data-testid="save-dialog">

      @if (idea()) {
        <div class="idea-title" data-testid="dialog-title">{{ idea()!.title }}</div>

        <div class="field">
          <label for="notes">Notes</label>
          <textarea id="notes" pTextarea [(ngModel)]="notes" rows="4"
            placeholder="Add notes about this idea..."
            [style]="{ width: '100%' }"
            data-testid="notes-input"></textarea>
        </div>

        <div class="field">
          <label>Tags</label>
          <div class="tag-input-wrapper">
            <div class="tags-display">
              @for (tag of tags; track tag) {
                <p-chip [label]="tag" [removable]="true" (onRemove)="removeTag(tag)" />
              }
            </div>
            <input type="text" pInputText
              [(ngModel)]="tagInput"
              (keydown.enter)="addTag()"
              placeholder="Type tag and press Enter"
              [style]="{ width: '100%' }"
              data-testid="tag-input" />
          </div>
        </div>
      }

      <ng-template #footer>
        <p-button label="Cancel" severity="secondary" (onClick)="onCancel()" data-testid="cancel-btn" />
        <p-button label="Save" (onClick)="onSave()" data-testid="save-btn" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .idea-title {
        font-size: 16px;
        font-weight: 600;
        color: #f0f6fc;
        margin-bottom: 16px;
        padding-bottom: 12px;
        border-bottom: 1px solid #21262d;
      }
      .field {
        margin-bottom: 16px;
      }
      .field label {
        display: block;
        font-size: 13px;
        font-weight: 600;
        color: #8b949e;
        margin-bottom: 6px;
      }
      .tag-input-wrapper {
        display: flex;
        flex-direction: column;
        gap: 8px;
      }
      .tags-display {
        display: flex;
        flex-wrap: wrap;
        gap: 4px;
      }
    `,
  ],
})
export class SaveIdeaDialogComponent implements OnChanges {
  private readonly store = inject(IdeaStore);

  readonly idea = input<Idea | null>(null);
  readonly visible = model(false);
  readonly saved = output<void>();

  notes = '';
  tags: string[] = [];
  tagInput = '';

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['idea'] && this.idea()) {
      this.notes = '';
      this.tags = [...(this.idea()!.tags ?? [])];
    }
  }

  addTag(): void {
    const tag = this.tagInput.trim();
    if (tag && !this.tags.includes(tag)) {
      this.tags = [...this.tags, tag];
    }
    this.tagInput = '';
  }

  removeTag(tag: string): void {
    this.tags = this.tags.filter((t) => t !== tag);
  }

  onSave(): void {
    if (!this.idea()) return;
    this.store.saveIdea(this.idea()!.id, this.notes || null, this.tags);
    this.close();
    this.saved.emit();
  }

  onCancel(): void {
    this.close();
  }

  private close(): void {
    this.visible.set(false);
    this.notes = '';
    this.tags = [];
    this.tagInput = '';
  }
}
