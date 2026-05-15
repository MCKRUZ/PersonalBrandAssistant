import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FeedItem, FeedActionResult, FeedItemType, FeedListParams } from '../models/feed-item.model';
import { FeedSummary } from '../models/feed-summary.model';
import { TrendingTopic } from '../models/trending-topic.model';
import { PagedResult } from '../../../models/pagination.model';

@Injectable({ providedIn: 'root' })
export class FeedService {
  private readonly baseUrl = '/api/feed';

  constructor(private readonly http: HttpClient) {}

  list(params: FeedListParams): Observable<PagedResult<FeedItem>> {
    let httpParams = new HttpParams();

    if (params.page != null) httpParams = httpParams.set('page', params.page.toString());
    if (params.pageSize != null) httpParams = httpParams.set('pageSize', params.pageSize.toString());
    if (params.type != null) httpParams = httpParams.set('type', params.type);
    if (params.priority != null) httpParams = httpParams.set('priority', params.priority);
    if (params.isRead != null) httpParams = httpParams.set('isRead', params.isRead.toString());
    if (params.includeExpired != null) httpParams = httpParams.set('includeExpired', params.includeExpired.toString());
    if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
    if (params.sortDirection) httpParams = httpParams.set('sortDirection', params.sortDirection);

    return this.http.get<PagedResult<FeedItem>>(this.baseUrl, { params: httpParams });
  }

  getSummary(): Observable<FeedSummary> {
    return this.http.get<FeedSummary>(`${this.baseUrl}/summary`);
  }

  getTrending(): Observable<TrendingTopic[]> {
    return this.http.get<TrendingTopic[]>(`${this.baseUrl}/trending`);
  }

  markRead(id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/read`, {});
  }

  actOnItem(id: string, action: string): Observable<FeedActionResult> {
    return this.http.put<FeedActionResult>(`${this.baseUrl}/${id}/act`, { action });
  }

  batchMarkRead(type?: FeedItemType, isRead?: boolean): Observable<{ count: number }> {
    return this.http.put<{ count: number }>(`${this.baseUrl}/batch/read`, { type, isRead });
  }

  batchDismiss(type: FeedItemType): Observable<{ count: number }> {
    return this.http.put<{ count: number }>(`${this.baseUrl}/batch/dismiss`, { type });
  }

  batchAct(ids: string[], action: string): Observable<{ successCount: number; failures: { id: string; reason: string }[] }> {
    return this.http.put<{ successCount: number; failures: { id: string; reason: string }[] }>(
      `${this.baseUrl}/batch/act`,
      { ids, action }
    );
  }
}
