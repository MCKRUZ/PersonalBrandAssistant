import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { TrendSuggestion } from '../../../shared/models';
import { InterestKeyword, NewsSource, SavedNewsItem, TrendSettings } from '../models/news.model';

@Injectable({ providedIn: 'root' })
export class NewsService {
  private readonly api = inject(ApiService);

  getSuggestions(limit = 100): Observable<TrendSuggestion[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.api.get<TrendSuggestion[]>('trends/suggestions', params);
  }

  acceptSuggestion(id: string): Observable<void> {
    return this.api.post<void>(`trends/suggestions/${id}/accept`, {});
  }

  dismissSuggestion(id: string): Observable<void> {
    return this.api.post<void>(`trends/suggestions/${id}/dismiss`, {});
  }

  refreshTrends(): Observable<void> {
    return this.api.post<void>('trends/refresh', {});
  }

  getSources(): Observable<NewsSource[]> {
    return this.api.get<NewsSource[]>('trends/sources');
  }

  toggleSource(id: string): Observable<void> {
    return this.api.patch<void>(`trends/sources/${id}/toggle`, {});
  }

  createSource(name: string, feedUrl: string, category?: string): Observable<NewsSource> {
    return this.api.post<NewsSource>('trends/sources', { name, feedUrl, category });
  }

  deleteSource(id: string): Observable<void> {
    return this.api.delete<void>(`trends/sources/${id}`);
  }

  getKeywords(): Observable<InterestKeyword[]> {
    return this.api.get<InterestKeyword[]>('trends/keywords');
  }

  addKeyword(keyword: string, weight = 1.0): Observable<InterestKeyword> {
    return this.api.post<InterestKeyword>('trends/keywords', { keyword, weight });
  }

  removeKeyword(id: string): Observable<void> {
    return this.api.delete<void>(`trends/keywords/${id}`);
  }

  getSavedItems(): Observable<SavedNewsItem[]> {
    return this.api.get<SavedNewsItem[]>('trends/saved');
  }

  saveItem(trendItemId: string): Observable<SavedNewsItem> {
    return this.api.post<SavedNewsItem>('trends/saved', { trendItemId });
  }

  removeSavedItem(id: string): Observable<void> {
    return this.api.delete<void>(`trends/saved/${id}`);
  }

  getTrendSettings(): Observable<TrendSettings> {
    return this.api.get<TrendSettings>('trends/settings');
  }

  updateTrendSettings(settings: TrendSettings): Observable<void> {
    return this.api.put<void>('trends/settings', settings);
  }

  analyzeItem(trendItemId: string): Observable<{ summary: string; imageUrl?: string }> {
    return this.api.post<{ summary: string; imageUrl?: string }>(`trends/items/${trendItemId}/analyze`, {});
  }
}
