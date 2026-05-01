import { Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardModule } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { BestTimesHeatmap } from '../heatmap.model';

const DAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const HOURS = Array.from({ length: 14 }, (_, i) => i + 6); // 6AM - 7PM

@Component({
  selector: 'app-best-times-heatmap',
  standalone: true,
  imports: [CommonModule, CardModule, TooltipModule],
  template: `
    <p-card header="Best Times to Post">
      @if (!heatmap() || heatmap()!.cells.length === 0) {
        <div class="empty">No posting time data available yet.</div>
      } @else {
        <div class="heatmap-grid">
          <div class="corner"></div>
          @for (hour of hours; track hour) {
            <div class="hour-label">{{ formatHour(hour) }}</div>
          }
          @for (day of dayLabels; track day; let d = $index) {
            <div class="day-label">{{ day }}</div>
            @for (hour of hours; track hour) {
              <div
                class="cell"
                [style.background-color]="getCellColor(d, hour)"
                [pTooltip]="getCellTooltip(d, hour)"
                tooltipPosition="top"
              ></div>
            }
          }
        </div>
      }
    </p-card>
  `,
  styles: `
    .heatmap-grid {
      display: grid;
      grid-template-columns: 3rem repeat(14, 1fr);
      gap: 2px;
    }
    .corner { }
    .hour-label {
      font-size: 0.7rem;
      text-align: center;
      color: var(--p-text-muted-color);
      padding-bottom: 0.25rem;
    }
    .day-label {
      font-size: 0.75rem;
      display: flex;
      align-items: center;
      color: var(--p-text-muted-color);
    }
    .cell {
      aspect-ratio: 1;
      border-radius: 3px;
      min-height: 24px;
      cursor: pointer;
      transition: transform 0.1s;
    }
    .cell:hover { transform: scale(1.2); }
    .empty {
      text-align: center;
      padding: 2rem;
      color: var(--p-text-muted-color);
    }
  `,
})
export class BestTimesHeatmapComponent {
  readonly heatmap = input<BestTimesHeatmap | null>(null);

  readonly dayLabels = DAY_LABELS;
  readonly hours = HOURS;

  private readonly cellMap = computed(() => {
    const map = new Map<string, number>();
    const h = this.heatmap();
    if (h) {
      for (const cell of h.cells) {
        map.set(`${cell.day}-${cell.hour}`, cell.engagement);
      }
    }
    return map;
  });

  getCellColor(day: number, hour: number): string {
    const h = this.heatmap();
    if (!h || h.maxEngagement === 0) return 'rgba(255,255,255,0.04)';
    const engagement = this.cellMap().get(`${day}-${hour}`) ?? 0;
    const intensity = engagement / h.maxEngagement;
    return intensity === 0
      ? 'rgba(255,255,255,0.04)'
      : `rgba(200, 113, 86, ${0.15 + intensity * 0.85})`;
  }

  getCellTooltip(day: number, hour: number): string {
    const engagement = this.cellMap().get(`${day}-${hour}`) ?? 0;
    return `${DAY_LABELS[day]} ${this.formatHour(hour)}: ${engagement} engagements`;
  }

  formatHour(hour: number): string {
    if (hour === 0) return '12a';
    if (hour < 12) return `${hour}a`;
    if (hour === 12) return '12p';
    return `${hour - 12}p`;
  }
}
