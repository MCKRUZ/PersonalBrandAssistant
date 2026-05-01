import { Component, input, output, computed } from '@angular/core';
import { SlotCardComponent } from './slot-card.component';
import { CalendarSlot } from '../../../shared/models';

@Component({
  selector: 'app-calendar-week-grid',
  standalone: true,
  imports: [SlotCardComponent],
  template: `
    <div class="week-grid">
      <div class="time-axis">
        <div class="axis-header"></div>
        @for (hour of hours; track hour) {
          <div class="hour-label">{{ hour }}:00</div>
        }
      </div>
      @for (day of days(); track day.dateKey) {
        <div class="day-column">
          <div class="day-header" [class.today]="day.isToday">
            <span class="day-name">{{ day.dayName }}</span>
            <span class="day-number">{{ day.dayNumber }}</span>
          </div>
          <div class="day-body">
            @for (slot of day.slots; track slot.id) {
              <app-slot-card [slot]="slot" (click)="slotClicked.emit(slot)" />
            }
            @if (day.slots.length === 0) {
              <button class="empty-add" (click)="emptySlotClicked.emit(day.date)">+</button>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    .week-grid {
      display: grid;
      grid-template-columns: 48px repeat(7, 1fr);
      border: 1px solid var(--p-surface-700);
      border-radius: 8px;
      overflow: hidden;
    }
    .time-axis {
      display: flex;
      flex-direction: column;
      background: var(--p-surface-900);
    }
    .axis-header { height: 48px; border-bottom: 1px solid var(--p-surface-700); }
    .hour-label {
      height: 48px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 0.7rem;
      color: var(--p-text-muted-color);
      border-bottom: 1px solid var(--p-surface-700);
    }
    .day-column {
      border-left: 1px solid var(--p-surface-700);
      min-width: 0;
    }
    .day-header {
      height: 48px;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      background: var(--p-surface-800);
      border-bottom: 1px solid var(--p-surface-700);
      font-family: 'DM Sans', sans-serif;

      &.today {
        background: color-mix(in srgb, var(--p-primary-color) 15%, var(--p-surface-800));
      }
    }
    .day-name { font-size: 0.7rem; color: var(--p-text-muted-color); text-transform: uppercase; }
    .day-number { font-size: 0.9rem; font-weight: 600; }
    .day-body {
      padding: 0.25rem;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      min-height: 200px;
    }
    .empty-add {
      width: 100%;
      padding: 0.5rem;
      border: 1px dashed var(--p-surface-600);
      border-radius: 4px;
      background: transparent;
      color: var(--p-text-muted-color);
      cursor: pointer;
      font-size: 1.2rem;
      transition: background 0.2s;

      &:hover { background: var(--p-surface-800); }
    }
  `,
})
export class CalendarWeekGridComponent {
  dateRange = input.required<{ from: string; to: string }>();
  slotsByDate = input.required<Map<string, CalendarSlot[]>>();

  slotClicked = output<CalendarSlot>();
  emptySlotClicked = output<Date>();

  readonly hours = Array.from({ length: 17 }, (_, i) => i + 6);
  readonly weekDays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

  days = computed(() => {
    const range = this.dateRange();
    const start = new Date(range.from);
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    return this.weekDays.map((name, i) => {
      const date = new Date(start);
      date.setDate(start.getDate() + i);
      const dateKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
      const d = new Date(date);
      d.setHours(0, 0, 0, 0);
      return {
        dayName: name,
        dayNumber: date.getDate(),
        dateKey,
        date,
        isToday: d.getTime() === today.getTime(),
        slots: this.slotsByDate().get(dateKey) ?? [],
      };
    });
  });
}
