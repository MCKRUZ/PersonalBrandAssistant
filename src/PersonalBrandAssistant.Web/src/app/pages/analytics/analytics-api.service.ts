import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  DashboardPeriod,
  DashboardSummary,
  DailyEngagement,
  PlatformSummary,
  WebsiteAnalyticsResponse,
  SubstackPost,
  periodToParams,
} from '../../features/analytics/models/dashboard.model';
import { ContentPerformanceReport, TopPerformingContent } from '../../shared/models';
import { BestTimesHeatmap } from './heatmap.model';

@Injectable({ providedIn: 'root' })
export class AnalyticsApiService {
  private readonly http = inject(HttpClient);

  getDashboardSummary(period: DashboardPeriod, refresh = false): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>('/api/analytics/dashboard', { params: periodToParams(period, refresh) });
  }

  getEngagementTimeline(period: DashboardPeriod, refresh = false): Observable<DailyEngagement[]> {
    return this.http.get<DailyEngagement[]>('/api/analytics/engagement-timeline', { params: periodToParams(period, refresh) });
  }

  getPlatformSummaries(period: DashboardPeriod, refresh = false): Observable<PlatformSummary[]> {
    return this.http.get<PlatformSummary[]>('/api/analytics/platform-summary', { params: periodToParams(period, refresh) });
  }

  getTopContent(from: string, to: string, limit = 10): Observable<TopPerformingContent[]> {
    const params = new HttpParams().set('from', from).set('to', to).set('limit', limit);
    return this.http.get<TopPerformingContent[]>('/api/analytics/top', { params });
  }

  getWebsiteAnalytics(period: DashboardPeriod, refresh = false): Observable<WebsiteAnalyticsResponse> {
    return this.http.get<WebsiteAnalyticsResponse>('/api/analytics/website', { params: periodToParams(period, refresh) });
  }

  getSubstackPosts(): Observable<SubstackPost[]> {
    return this.http.get<SubstackPost[]>('/api/analytics/substack');
  }

  getContentReport(contentId: string): Observable<ContentPerformanceReport> {
    return this.http.get<ContentPerformanceReport>(`/api/analytics/content/${contentId}`);
  }

  refreshAnalytics(contentId: string): Observable<void> {
    return this.http.post<void>(`/api/analytics/content/${contentId}/refresh`, {});
  }

  getBestTimesHeatmap(period: DashboardPeriod): Observable<BestTimesHeatmap> {
    const params = periodToParams(period);
    return this.http.get<BestTimesHeatmap>('/api/analytics/best-times', { params });
  }
}
