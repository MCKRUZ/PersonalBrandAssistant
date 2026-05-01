import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ContentItem, ContentType } from '../../core/models/content.model';
import { BrandVoiceScore } from '../../core/models/brand-voice.model';
import { AgentExecution } from '../../core/models/agent.model';
import { PlatformType } from '../../core/models/platform.model';

export interface CreateContentRequest {
  readonly title: string;
  readonly body: string;
  readonly type: ContentType;
  readonly platform: PlatformType;
}

export interface UpdateContentRequest {
  readonly title?: string;
  readonly body?: string;
  readonly type?: ContentType;
  readonly platform?: PlatformType;
}

@Injectable()
export class ContentEditorApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  getById(id: string): Observable<ContentItem> {
    return this.http.get<ContentItem>(`${this.base}/content/${id}`);
  }

  create(request: CreateContentRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.base}/content`, request);
  }

  update(id: string, request: UpdateContentRequest, version: number): Observable<void> {
    const headers = new HttpHeaders({ 'If-Match': `"${version}"` });
    return this.http.put<void>(`${this.base}/content/${id}`, request, { headers });
  }

  scoreContent(contentId: string): Observable<BrandVoiceScore> {
    return this.http.post<BrandVoiceScore>(`${this.base}/brand-voice/score`, { contentId });
  }

  approve(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/approval/${id}/approve`, {});
  }

  publish(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/content-pipeline/${id}/publish`, {});
  }

  schedule(id: string, scheduledAt: string): Observable<void> {
    return this.http.post<void>(`${this.base}/scheduling/${id}/schedule`, { scheduledAt });
  }

  getExecutionHistory(contentId: string): Observable<readonly AgentExecution[]> {
    return this.http.get<AgentExecution[]>(`${this.base}/agents/executions`, {
      params: { contentId },
    });
  }
}
