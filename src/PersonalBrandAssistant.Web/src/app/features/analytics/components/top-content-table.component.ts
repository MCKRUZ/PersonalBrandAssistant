import { Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TableModule } from 'primeng/table';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { PlatformChipComponent } from '../../../shared/components/platform-chip/platform-chip.component';
import { TopPerformingContent } from '../../../shared/models';

@Component({
  selector: 'app-top-content-table',
  standalone: true,
  imports: [CommonModule, TableModule, Card, Tag, ButtonModule, PlatformChipComponent],
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
            <td>
              <p-button icon="pi pi-chart-bar" [text]="true" (onClick)="viewDetail.emit(item.contentId)" />
            </td>
          </tr>
        </ng-template>
      </p-table>
    </p-card>
  `,
})
export class TopContentTableComponent {
  items = input<readonly TopPerformingContent[]>([]);
  mutableItems = computed(() => [...this.items()]);
  viewDetail = output<string>();
}
