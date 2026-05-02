import { Component, OnInit, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Button } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';
import { KpiCardComponent } from '../../shared/components/kpi-card/kpi-card.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { DashboardStore } from './dashboard.store';
import { DashboardApiService, AiSuggestion } from './dashboard-api.service';
import { CalendarSlot } from '../../core/models/calendar.model';
import { ContentItem } from '../../core/models/content.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [Button, Skeleton, KpiCardComponent, StatusBadgeComponent],
  providers: [DashboardStore, DashboardApiService],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  readonly store = inject(DashboardStore);
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.store.load();
  }

  navigateToContent(id: string): void {
    this.router.navigate(['/content', id, 'edit']);
  }

  navigateToNew(suggestion?: AiSuggestion): void {
    if (suggestion) {
      this.router.navigate(['/content'], {
        queryParams: { topic: suggestion.topic, platform: suggestion.platform },
      });
    } else {
      this.router.navigate(['/content']);
    }
  }

  navigateToCalendar(): void {
    this.router.navigate(['/calendar']);
  }

  formatTime(scheduledAt: string): string {
    const d = new Date(scheduledAt);
    const h = d.getHours();
    const m = d.getMinutes();
    const ampm = h >= 12 ? 'PM' : 'AM';
    const h12 = h % 12 || 12;
    return `${h12}:${m.toString().padStart(2, '0')} ${ampm}`;
  }

  formatCost(cost: number | undefined): string {
    return `$${(cost ?? 0).toFixed(2)}`;
  }

  relativeDate(dateStr: string): string {
    const now = Date.now();
    const date = new Date(dateStr).getTime();
    const diffMs = now - date;
    const diffMin = Math.floor(diffMs / 60000);
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHr = Math.floor(diffMin / 60);
    if (diffHr < 24) return `${diffHr}h ago`;
    const diffDay = Math.floor(diffHr / 24);
    if (diffDay === 1) return 'yesterday';
    return `${diffDay}d ago`;
  }

  trackSlot(_i: number, slot: CalendarSlot): string {
    return slot.id ?? `${_i}`;
  }

  trackItem(_i: number, item: ContentItem): string {
    return item.id;
  }

  trackSuggestion(_i: number, s: AiSuggestion): string {
    return `${s.topic}::${s.platform}`;
  }
}
