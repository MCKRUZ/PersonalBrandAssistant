import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { ContentPerformanceReport, TopPerformingContent } from '../../../shared/models';

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
}
