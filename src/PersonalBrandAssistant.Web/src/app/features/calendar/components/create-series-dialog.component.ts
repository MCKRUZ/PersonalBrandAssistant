import { Component, inject, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Dialog } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { Select } from 'primeng/select';
import { MultiSelect } from 'primeng/multiselect';
import { DatePicker } from 'primeng/datepicker';
import { ButtonModule } from 'primeng/button';
import { MessageService } from 'primeng/api';
import { CalendarService } from '../services/calendar.service';
import { ContentType, PlatformType } from '../../../shared/models';

@Component({
  selector: 'app-create-series-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, Dialog, InputTextModule, TextareaModule,
    Select, MultiSelect, DatePicker, ButtonModule,
  ],
  template: `
    <p-dialog header="New Content Series" [(visible)]="visible" [modal]="true" [style]="{ width: '500px' }">
      <form [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="flex flex-column gap-3">
          <div>
            <label class="block font-semibold mb-1">Name</label>
            <input pInputText formControlName="name" class="w-full" />
          </div>
          <div>
            <label class="block font-semibold mb-1">Description</label>
            <textarea pTextarea formControlName="description" class="w-full" [rows]="3"></textarea>
          </div>
          <div>
            <label class="block font-semibold mb-1">Recurrence</label>
            <p-select formControlName="recurrenceRule" [options]="recurrenceOptions" optionLabel="label" optionValue="value" styleClass="w-full" />
          </div>
          <div>
            <label class="block font-semibold mb-1">Content Type</label>
            <p-select formControlName="contentType" [options]="contentTypes" optionLabel="label" optionValue="value" styleClass="w-full" />
          </div>
          <div>
            <label class="block font-semibold mb-1">Platforms</label>
            <p-multiselect formControlName="targetPlatforms" [options]="platforms" optionLabel="label" optionValue="value" styleClass="w-full" />
          </div>
          <div>
            <label class="block font-semibold mb-1">Start Date</label>
            <p-datepicker formControlName="startsAt" styleClass="w-full" />
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
export class CreateSeriesDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly calendarService = inject(CalendarService);
  private readonly messageService = inject(MessageService);

  created = output<void>();
  visible = false;
  saving = false;

  readonly recurrenceOptions = [
    { label: 'Daily', value: 'FREQ=DAILY' },
    { label: 'Weekly', value: 'FREQ=WEEKLY' },
    { label: 'Bi-Weekly', value: 'FREQ=WEEKLY;INTERVAL=2' },
    { label: 'Monthly', value: 'FREQ=MONTHLY' },
    { label: 'Mon/Wed/Fri', value: 'FREQ=WEEKLY;BYDAY=MO,WE,FR' },
    { label: 'Tue/Thu', value: 'FREQ=WEEKLY;BYDAY=TU,TH' },
  ];

  readonly contentTypes = [
    { label: 'Blog Post', value: 'BlogPost' as ContentType },
    { label: 'Social Post', value: 'SocialPost' as ContentType },
    { label: 'Thread', value: 'Thread' as ContentType },
    { label: 'Video Description', value: 'VideoDescription' as ContentType },
  ];

  readonly platforms = [
    { label: 'Twitter/X', value: 'TwitterX' as PlatformType },
    { label: 'LinkedIn', value: 'LinkedIn' as PlatformType },
    { label: 'Instagram', value: 'Instagram' as PlatformType },
    { label: 'YouTube', value: 'YouTube' as PlatformType },
  ];

  readonly form = this.fb.group({
    name: ['', Validators.required],
    description: [''],
    recurrenceRule: ['FREQ=WEEKLY', Validators.required],
    contentType: ['SocialPost' as ContentType, Validators.required],
    targetPlatforms: [[] as PlatformType[], Validators.required],
    startsAt: [new Date(), Validators.required],
  });

  open() {
    this.visible = true;
    this.form.reset({ recurrenceRule: 'FREQ=WEEKLY', contentType: 'SocialPost', targetPlatforms: [], startsAt: new Date() });
  }

  onSubmit() {
    if (this.form.invalid) return;
    this.saving = true;
    const val = this.form.getRawValue();
    this.calendarService.createSeries({
      name: val.name!,
      description: val.description || undefined,
      recurrenceRule: val.recurrenceRule!,
      contentType: val.contentType!,
      targetPlatforms: val.targetPlatforms!,
      themeTags: [],
      timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
      startsAt: (val.startsAt as Date).toISOString(),
    }).subscribe({
      next: () => {
        this.saving = false;
        this.visible = false;
        this.messageService.add({ severity: 'success', summary: 'Created', detail: 'Series created' });
        this.created.emit();
      },
      error: () => { this.saving = false; },
    });
  }
}
