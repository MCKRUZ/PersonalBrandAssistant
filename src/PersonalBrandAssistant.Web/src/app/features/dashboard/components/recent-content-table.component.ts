import { Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { Card } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { Content } from '../../../shared/models';

@Component({
  selector: 'app-recent-content-table',
  standalone: true,
  imports: [CommonModule, RouterLink, TableModule, Card, ButtonModule, StatusBadgeComponent, RelativeTimePipe],
  template: `
    <p-card>
      <div class="flex justify-content-between align-items-center mb-3">
        <h3 class="m-0">Recent Content</h3>
        <p-button label="View All" [text]="true" icon="pi pi-arrow-right" iconPos="right" routerLink="/content" />
      </div>
      <p-table [value]="mutableItems()" [rowHover]="true" styleClass="p-datatable-sm">
        <ng-template #header>
          <tr>
            <th>Title</th>
            <th>Status</th>
            <th>Created</th>
          </tr>
        </ng-template>
        <ng-template #body let-item>
          <tr>
            <td>{{ item.title || 'Untitled' }}</td>
            <td><app-status-badge [status]="item.status" /></td>
            <td>{{ item.createdAt | relativeTime }}</td>
          </tr>
        </ng-template>
      </p-table>
    </p-card>
  `,
})
export class RecentContentTableComponent {
  items = input<readonly Content[]>([]);
  mutableItems = computed(() => [...this.items()]);
}
