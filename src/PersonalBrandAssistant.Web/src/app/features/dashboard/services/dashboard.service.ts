import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { forkJoin, Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { Content, PagedResult, TrendSuggestion, CalendarSlot, Notification } from '../../../shared/models';

export interface DashboardData {
  readonly recentContent: PagedResult<Content>;
  readonly trendSuggestions: TrendSuggestion[];
  readonly upcomingSlots: CalendarSlot[];
  readonly notifications: Notification[];
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly api = inject(ApiService);

  loadAll(): Observable<DashboardData> {
    const now = new Date();
    const in7Days = new Date(now.getTime() + 7 * 86_400_000);

    return forkJoin({
      recentContent: this.api.get<PagedResult<Content>>('content', new HttpParams().set('pageSize', '5')),
      trendSuggestions: this.api.get<TrendSuggestion[]>('trends/suggestions', new HttpParams().set('limit', '5')),
      upcomingSlots: this.api.get<CalendarSlot[]>('calendar', new HttpParams().set('from', now.toISOString()).set('to', in7Days.toISOString())),
      notifications: this.api.get<Notification[]>('notifications', new HttpParams().set('pageSize', '10')),
    });
  }

  getPendingReviewCount(): Observable<Content[]> {
    return this.api.get<Content[]>('approval/pending', new HttpParams().set('pageSize', '1'));
  }
}
