import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ContentDetail,
  Content,
  ContentFilterState,
  CreateContentRequest,
  UpdateContentRequest,
  DraftContentRequest,
  ScheduleContentRequest,
  CrossPostRequest,
  VoiceCheckResult,
  Platform,
} from '../models/content.model';
import type {
  PublishRequest,
  PublishStatusResponse,
  PlatformConnectionStatus,
} from '../models/content.model';
import { PagedResult } from '../../../models/pagination.model';

@Injectable({ providedIn: 'root' })
export class ContentService {
  private readonly baseUrl = '/api/content';

  constructor(private readonly http: HttpClient) {}

  list(
    filter: Partial<ContentFilterState>,
    page: number,
    pageSize: number
  ): Observable<PagedResult<Content>> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (filter.status) params = params.set('status', filter.status);
    if (filter.platform) params = params.set('platform', filter.platform);
    if (filter.contentType) params = params.set('contentType', filter.contentType);
    if (filter.dateFrom) params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo) params = params.set('dateTo', filter.dateTo);
    if (filter.search) params = params.set('search', filter.search);

    return this.http.get<PagedResult<Content>>(this.baseUrl, { params });
  }

  get(id: string): Observable<ContentDetail> {
    return this.http.get<ContentDetail>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateContentRequest): Observable<string> {
    return this.http.post<string>(this.baseUrl, request);
  }

  update(id: string, request: UpdateContentRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  draft(id: string, request: DraftContentRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/draft`, request);
  }

  crossPost(id: string, request: CrossPostRequest): Observable<string> {
    return this.http.post<string>(`${this.baseUrl}/${id}/cross-post`, request);
  }

  approve(id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/approve`, {});
  }

  submitForReview(id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/submit-review`, {});
  }

  requestChanges(id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/request-changes`, {});
  }

  schedule(id: string, request: ScheduleContentRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/schedule`, request);
  }

  unschedule(id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/unschedule`, {});
  }

  publish(id: string, request?: PublishRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/publish`, request ?? {});
  }

  unpublish(id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/unpublish`, {});
  }

  restore(id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/restore`, {});
  }

  voiceCheck(id: string): Observable<VoiceCheckResult> {
    return this.http.get<VoiceCheckResult>(`${this.baseUrl}/${id}/voice-check`);
  }

  getPublishStatus(id: string): Observable<PublishStatusResponse> {
    return this.http.get<PublishStatusResponse>(`${this.baseUrl}/${id}/publish-status`);
  }

  retryPlatform(id: string, platform: Platform): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/retry/${platform}`, {});
  }

  getPlatforms(): Observable<PlatformConnectionStatus[]> {
    return this.http.get<PlatformConnectionStatus[]>('/api/platforms');
  }
}
