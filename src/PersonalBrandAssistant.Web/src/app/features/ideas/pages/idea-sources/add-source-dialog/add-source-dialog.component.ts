import { Component, inject, input, output, model, OnChanges, SimpleChanges } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { IdeaSourceStore } from '../../../store/idea-source.store';
import { IdeaSource, IdeaSourceType } from '../../../../../models/idea.model';

@Component({
  selector: 'app-add-source-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogModule, ButtonModule, InputTextModule, InputNumberModule, SelectModule],
  template: `
    <p-dialog
      [visible]="visible()"
      (visibleChange)="visible.set($event)"
      [modal]="true"
      [header]="editSource() ? 'Edit Source' : 'Add Source'"
      [style]="{ width: '500px' }"
      data-testid="source-dialog">

      <form [formGroup]="form" data-testid="source-form">
        <div class="field">
          <label for="name">Name *</label>
          <input id="name" type="text" pInputText formControlName="name"
            [style]="{ width: '100%' }" data-testid="name-input" />
        </div>

        <div class="field">
          <label for="type">Type *</label>
          <p-select id="type" formControlName="type" [options]="typeOptions"
            optionLabel="label" optionValue="value"
            [style]="{ width: '100%' }" data-testid="type-select" />
        </div>

        @if (form.get('type')?.value === 'RSS') {
          <div class="field">
            <label for="feedUrl">Feed URL *</label>
            <input id="feedUrl" type="text" pInputText formControlName="feedUrl"
              placeholder="https://example.com/rss"
              [style]="{ width: '100%' }" data-testid="feed-url-input" />
          </div>
        }

        @if (form.get('type')?.value === 'API') {
          <div class="field">
            <label for="apiUrl">API URL</label>
            <input id="apiUrl" type="text" pInputText formControlName="apiUrl"
              placeholder="https://api.example.com"
              [style]="{ width: '100%' }" data-testid="api-url-input" />
          </div>
        }

        <div class="field">
          <label for="category">Category</label>
          <input id="category" type="text" pInputText formControlName="category"
            [style]="{ width: '100%' }" />
        </div>

        <div class="field">
          <label for="pollInterval">Poll Interval (minutes)</label>
          <p-inputNumber id="pollInterval" formControlName="pollIntervalMinutes"
            [min]="5" [max]="1440" [style]="{ width: '100%' }" />
        </div>
      </form>

      <ng-template #footer>
        <p-button label="Cancel" severity="secondary" (onClick)="onCancel()" />
        <p-button [label]="editSource() ? 'Update' : 'Create'" (onClick)="onSubmit()"
          [disabled]="form.invalid" data-testid="submit-btn" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
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
    `,
  ],
})
export class AddSourceDialogComponent implements OnChanges {
  private readonly store = inject(IdeaSourceStore);
  private readonly fb = inject(FormBuilder);

  readonly editSource = input<IdeaSource | null>(null);
  readonly visible = model(false);
  readonly saved = output<void>();

  readonly typeOptions = [
    { label: 'RSS Feed', value: IdeaSourceType.RSS },
    { label: 'API', value: IdeaSourceType.API },
    { label: 'Manual', value: IdeaSourceType.Manual },
    { label: 'AI Generated', value: IdeaSourceType.AIGenerated },
  ];

  readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    type: [IdeaSourceType.RSS, Validators.required],
    feedUrl: [''],
    apiUrl: [''],
    category: [''],
    pollIntervalMinutes: [30, [Validators.required, Validators.min(5), Validators.max(1440)]],
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['editSource']) {
      const src = this.editSource();
      if (src) {
        this.form.patchValue({
          name: src.name,
          type: src.type,
          feedUrl: src.feedUrl ?? '',
          apiUrl: src.apiUrl ?? '',
          category: src.category,
          pollIntervalMinutes: src.pollIntervalMinutes,
        });
      } else {
        this.form.reset({ type: IdeaSourceType.RSS, pollIntervalMinutes: 30 });
      }
    }
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    const src = this.editSource();
    if (src) {
      this.store.update(src.id, {
        name: value.name!,
        type: value.type!,
        feedUrl: value.feedUrl || undefined,
        apiUrl: value.apiUrl || undefined,
        category: value.category!,
        pollIntervalMinutes: value.pollIntervalMinutes!,
      });
    } else {
      this.store.create({
        name: value.name!,
        type: value.type!,
        feedUrl: value.feedUrl || undefined,
        category: value.category!,
        pollIntervalMinutes: value.pollIntervalMinutes!,
      });
    }
    this.close();
    this.saved.emit();
  }

  onCancel(): void {
    this.close();
  }

  private close(): void {
    this.visible.set(false);
    this.form.reset({ type: IdeaSourceType.RSS, pollIntervalMinutes: 30 });
  }
}
