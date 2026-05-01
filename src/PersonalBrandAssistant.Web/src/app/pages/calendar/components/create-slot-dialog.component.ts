import { Component, inject, output } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Dialog } from 'primeng/dialog';
import { DatePicker } from 'primeng/datepicker';
import { Select } from 'primeng/select';
import { Button } from 'primeng/button';
import { MessageService } from 'primeng/api';
import { CalendarApiService } from '../calendar-api.service';
import { PlatformType } from '../../../shared/models';

@Component({
  selector: 'app-create-slot-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, Dialog, DatePicker, Select, Button],
  template: `
    <p-dialog header="New Slot" [(visible)]="visible" [modal]="true" [style]="{ width: '400px' }">
      <form [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="flex flex-column gap-3">
          <div>
            <label class="block font-semibold mb-1">Date & Time</label>
            <p-datepicker formControlName="scheduledAt" [showTime]="true" styleClass="w-full" />
          </div>
          <div>
            <label class="block font-semibold mb-1">Platform</label>
            <p-select formControlName="platform" [options]="platforms" optionLabel="label" optionValue="value" styleClass="w-full" />
          </div>
          <div class="flex justify-content-end gap-2">
            <p-button label="Cancel" severity="secondary" (onClick)="visible = false" />
            <p-button label="Create" icon="pi pi-check" type="submit" [loading]="saving" [disabled]="form.invalid" />
          </div>
        </div>
      </form>
    </p-dialog>
  `,
})
export class CreateSlotDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly calendarApi = inject(CalendarApiService);
  private readonly messageService = inject(MessageService);

  created = output<void>();
  visible = false;
  saving = false;

  readonly platforms = [
    { label: 'Twitter/X', value: 'TwitterX' as PlatformType },
    { label: 'LinkedIn', value: 'LinkedIn' as PlatformType },
    { label: 'Instagram', value: 'Instagram' as PlatformType },
    { label: 'YouTube', value: 'YouTube' as PlatformType },
    { label: 'Reddit', value: 'Reddit' as PlatformType },
    { label: 'Personal Blog', value: 'PersonalBlog' as PlatformType },
    { label: 'Substack', value: 'Substack' as PlatformType },
  ];

  readonly form = this.fb.group({
    scheduledAt: [new Date(), Validators.required],
    platform: ['TwitterX' as PlatformType, Validators.required],
  });

  open(date?: Date) {
    this.visible = true;
    this.form.reset({ scheduledAt: date ?? new Date(), platform: 'TwitterX' });
  }

  onSubmit() {
    if (this.form.invalid) return;
    this.saving = true;
    const val = this.form.getRawValue();
    this.calendarApi.createSlot({
      scheduledAt: (val.scheduledAt as Date).toISOString(),
      platform: val.platform!,
    }).subscribe({
      next: () => {
        this.saving = false;
        this.visible = false;
        this.messageService.add({ severity: 'success', summary: 'Created', detail: 'Slot created' });
        this.created.emit();
      },
      error: () => {
        this.saving = false;
        this.messageService.add({ severity: 'error', summary: 'Error', detail: 'Failed to create slot' });
      },
    });
  }
}
