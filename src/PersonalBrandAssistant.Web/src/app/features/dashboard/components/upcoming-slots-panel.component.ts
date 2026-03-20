import { Component, input } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Card } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { PlatformChipComponent } from '../../../shared/components/platform-chip/platform-chip.component';
import { CalendarSlot } from '../../../shared/models';

@Component({
  selector: 'app-upcoming-slots-panel',
  standalone: true,
  imports: [CommonModule, Card, ButtonModule, Tag, PlatformChipComponent, DatePipe],
  template: `
    <p-card>
      <div class="flex justify-content-between align-items-center mb-3">
        <h3 class="m-0">Upcoming Schedule</h3>
        <p-button label="Calendar" [text]="true" icon="pi pi-calendar" routerLink="/calendar" />
      </div>
      @for (slot of items(); track slot.id) {
        <div class="flex justify-content-between align-items-center py-2 border-bottom-1 surface-border">
          <div class="flex align-items-center gap-2">
            <app-platform-chip [platform]="slot.platform" />
            <p-tag [value]="slot.status" [severity]="slot.status === 'Open' ? 'warn' : 'success'" />
          </div>
          <span class="text-color-secondary text-sm">{{ slot.scheduledAt | date:'short' }}</span>
        </div>
      }
      @if (items().length === 0) {
        <div class="text-center text-color-secondary py-3">No upcoming slots</div>
      }
    </p-card>
  `,
})
export class UpcomingSlotsPanelComponent {
  items = input<readonly CalendarSlot[]>([]);
}
