import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AnalyticsHealth, AnalyticsPeriod, WebsiteAnalytics } from '../models/analytics.model';

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
}
