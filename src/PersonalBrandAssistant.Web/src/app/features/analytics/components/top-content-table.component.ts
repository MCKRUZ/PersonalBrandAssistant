import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { TableModule } from 'primeng/table';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { PlatformChipComponent } from '../../../shared/components/platform-chip/platform-chip.component';
import { TopPerformingContent } from '../../../shared/models';

@Component({
  selector: 'app-top-content-table',
  standalone: true,
  imports: [CommonModule, TableModule, Card, Tag, ButtonModule, PlatformChipComponent, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-card header="Top Performing Content">
      <p-table [value]="mutableItems()" [rowHover]="true" styleClass="p-datatable-sm">
        <ng-template #header>
          <tr>
            <th style="width: 3rem">#</th>
            <th>Title</th>
            <th>Type</th>
            <th>Platforms</th>
            <th>Engagement</th>
            <th>Impressions</th>
            <th>Eng. Rate</th>
            <th style="width: 5rem"></th>
          </tr>
        </ng-template>
        <ng-template #body let-item let-i="rowIndex">
          <tr>
            <td class="font-bold">{{ i + 1 }}</td>
            <td>{{ item.title || 'Untitled' }}</td>
            <td><p-tag [value]="item.contentType" severity="info" /></td>
            <td>
              <div class="flex gap-1">
                @for (p of item.platforms; track p) {
                  <app-platform-chip [platform]="p" />
                }
              </div>
            </td>
            <td class="font-bold">{{ item.totalEngagement | number }}</td>
            <td>{{ item.impressions != null ? (item.impressions | number) : '--' }}</td>
            <td>
              @if (item.engagementRate != null) {
                <span class="eng-rate" [ngClass]="getEngagementRateClass(item.engagementRate)">
                  {{ item.engagementRate | number:'1.1-1' }}%
                </span>
              } @else {
                <span class="text-color-secondary">N/A</span>
              }
            </td>
            <td>
              <p-button icon="pi pi-chart-bar" [text]="true" (onClick)="viewDetail.emit(item.contentId)" />
            </td>
          </tr>
        </ng-template>
      </p-table>
    </p-card>
  `,
  styles: `
    .eng-rate {
      font-weight: 700;
      padding: 0.15rem 0.5rem;
      border-radius: 6px;
      font-size: 0.72rem;
    }
    .eng-rate.high { color: #22c55e; background: rgba(34, 197, 94, 0.12); }
    .eng-rate.med { color: #eab308; background: rgba(234, 179, 8, 0.12); }
    .eng-rate.low { color: #71717a; background: rgba(255, 255, 255, 0.04); }
  `,
})
export class TopContentTableComponent {
  readonly items = input<readonly TopPerformingContent[]>([]);
  readonly mutableItems = computed(() => [...this.items()]);
  readonly viewDetail = output<string>();

  getEngagementRateClass(rate: number | undefined): string {
    if (rate == null) return '';
    if (rate >= 5) return 'high';
    if (rate >= 3) return 'med';
    return 'low';
  }
}
