import { Component, inject, output } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Dialog } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { Tag } from 'primeng/tag';
import { MessageService } from 'primeng/api';
import { PlatformChipComponent } from '../../../shared/components/platform-chip/platform-chip.component';
import { CalendarService } from '../services/calendar.service';
import { CalendarSlot } from '../../../shared/models';

@Component({
  selector: 'app-slot-detail-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, Dialog, ButtonModule, InputTextModule, Tag, PlatformChipComponent, DatePipe],
  template: `
    <p-dialog header="Slot Details" [(visible)]="visible" [modal]="true" [style]="{ width: '400px' }">
      @if (slot) {
        <div class="flex flex-column gap-3">
          <div>
            <span class="font-semibold">Platform:</span>
            <app-platform-chip [platform]="slot.platform" />
          </div>
          <div>
            <span class="font-semibold">Scheduled:</span>
            {{ slot.scheduledAt | date:'medium' }}
          </div>
          <div>
            <span class="font-semibold">Status:</span>
            <p-tag [value]="slot.status" [severity]="slot.status === 'Open' ? 'warn' : 'success'" />
          </div>

          @if (slot.status === 'Open') {
            <div>
              <label class="block font-semibold mb-1">Assign Content ID</label>
              <input pInputText [(ngModel)]="contentIdToAssign" class="w-full" placeholder="Content ID" />
            </div>
            <p-button label="Assign" icon="pi pi-link" (onClick)="assignContent()" [disabled]="!contentIdToAssign" [loading]="assigning" />
          }
        </div>
      }
    </p-dialog>
  `,
})
export class SlotDetailDialogComponent {
  private readonly calendarService = inject(CalendarService);
  private readonly messageService = inject(MessageService);

  assigned = output<void>();

  visible = false;
  slot?: CalendarSlot;
  contentIdToAssign = '';
  assigning = false;

  open(slot: CalendarSlot) {
    this.slot = slot;
    this.contentIdToAssign = slot.contentId ?? '';
    this.visible = true;
  }

  assignContent() {
    if (!this.slot || !this.contentIdToAssign) return;
    this.assigning = true;
    this.calendarService.assignContent(this.slot.id, this.contentIdToAssign).subscribe({
      next: () => {
        this.assigning = false;
        this.visible = false;
        this.messageService.add({ severity: 'success', summary: 'Assigned', detail: 'Content assigned to slot' });
        this.assigned.emit();
      },
      error: () => { this.assigning = false; },
    });
  }
}
