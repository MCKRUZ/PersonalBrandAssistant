import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { ContentItem } from '../../core/models/content.model';
import { CalendarSlot } from '../../core/models/calendar.model';
import { PlatformType } from '../../core/models/platform.model';

export interface DashboardKpis {
  readonly pendingCount: number;
  readonly publishedCount: number;
  readonly reach: number;
  readonly aiCost: number;
}

export interface AiSuggestion {
  readonly topic: string;
  readonly platform: PlatformType;
  readonly source: string;
}

@Injectable()
export class DashboardApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  getKpis(): Observable<DashboardKpis> {
    return this.http.get<DashboardKpis>(`${this.base}/analytics/dashboard`);
  }

  getTodaySchedule(): Observable<CalendarSlot[]> {
    const today = new Date();
    const from = today.toISOString().split('T')[0];
    const tomorrow = new Date(today);
    tomorrow.setDate(tomorrow.getDate() + 1);
    const to = tomorrow.toISOString().split('T')[0];
    return this.http.get<CalendarSlot[]>(`${this.base}/calendar`, {
      params: { from, to },
    });
  }

  getRecentItems(): Observable<ContentItem[]> {
    return this.http.get<{ items: ContentItem[] }>(`${this.base}/content`, {
      params: { sort: 'created', order: 'desc', take: '10' },
    }).pipe(map((res) => res.items ?? []));
  }

  getSuggestions(): Observable<AiSuggestion[]> {
    return this.http.get<AiSuggestion[]>(`${this.base}/integration/briefing/summary`);
  }
}
