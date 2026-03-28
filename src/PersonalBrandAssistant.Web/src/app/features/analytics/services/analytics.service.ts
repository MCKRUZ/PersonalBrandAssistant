import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { ContentPerformanceReport, TopPerformingContent } from '../../../shared/models';
import {
  DashboardPeriod,
  DashboardSummary,
  DailyEngagement,
  PlatformSummary,
  WebsiteAnalyticsResponse,
  SubstackPost,
  periodToParams,
} from '../models/dashboard.model';

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly api = inject(ApiService);

  getContentReport(contentId: string): Observable<ContentPerformanceReport> {
    return this.api.get<ContentPerformanceReport>(`analytics/content/${contentId}`);
  }

  getTopContent(from: string, to: string, limit = 10): Observable<TopPerformingContent[]> {
    const params = new HttpParams()
      .set('from', from)
      .set('to', to)
      .set('limit', limit.toString());
    return this.api.get<TopPerformingContent[]>('analytics/top', params);
  }

  refreshAnalytics(contentId: string): Observable<void> {
    return this.api.post<void>(`analytics/content/${contentId}/refresh`, {});
  }

  getDashboardSummary(period: DashboardPeriod, refresh = false): Observable<DashboardSummary> {
    return this.api.get<DashboardSummary>('analytics/dashboard', periodToParams(period, refresh));
  }

  getEngagementTimeline(period: DashboardPeriod, refresh = false): Observable<DailyEngagement[]> {
    return this.api.get<DailyEngagement[]>('analytics/engagement-timeline', periodToParams(period, refresh));
  }

  getPlatformSummaries(period: DashboardPeriod, refresh = false): Observable<PlatformSummary[]> {
    return this.api.get<PlatformSummary[]>('analytics/platform-summary', periodToParams(period, refresh));
  }

  getWebsiteAnalytics(period: DashboardPeriod, refresh = false): Observable<WebsiteAnalyticsResponse> {
    return this.api.get<WebsiteAnalyticsResponse>('analytics/website', periodToParams(period, refresh));
  }

  getSubstackPosts(): Observable<SubstackPost[]> {
    return this.api.get<SubstackPost[]>('analytics/substack');
  }
}
