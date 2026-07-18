import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AnalyticsHealth, AnalyticsPeriod, WebsiteAnalytics } from '../models/analytics.model';
import {
  AnalyticsOverview,
  ChannelAnalytics,
  ChannelPlatform,
  YouTubeDeepAnalytics,
} from '../models/channel-analytics.model';

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly baseUrl = '/api/analytics';

  constructor(private readonly http: HttpClient) {}

  getWebsite(period: AnalyticsPeriod): Observable<WebsiteAnalytics> {
    const params = new HttpParams().set('period', period);
    return this.http.get<WebsiteAnalytics>(`${this.baseUrl}/website`, { params });
  }

  getHealth(): Observable<AnalyticsHealth> {
    return this.http.get<AnalyticsHealth>(`${this.baseUrl}/health`);
  }

  getOverview(period: AnalyticsPeriod): Observable<AnalyticsOverview> {
    const params = new HttpParams().set('period', period);
    return this.http.get<AnalyticsOverview>(`${this.baseUrl}/overview`, { params });
  }

  getChannel(platform: ChannelPlatform, period: AnalyticsPeriod): Observable<ChannelAnalytics> {
    const params = new HttpParams().set('period', period);
    return this.http.get<ChannelAnalytics>(`${this.baseUrl}/channel/${platform}`, { params });
  }

  getYouTubeDeep(period: AnalyticsPeriod): Observable<YouTubeDeepAnalytics> {
    const params = new HttpParams().set('period', period);
    return this.http.get<YouTubeDeepAnalytics>(`${this.baseUrl}/youtube/deep`, { params });
  }
}
