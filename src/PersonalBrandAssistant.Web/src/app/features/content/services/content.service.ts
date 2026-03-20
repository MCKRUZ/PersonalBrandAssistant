import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  Content, CreateContentRequest, UpdateContentRequest, ContentCreationRequest,
  PagedResult, ContentStatus, ContentType, BrandVoiceScore, WorkflowTransitionLog,
  TransitionRequest, RepurposingSuggestion, PlatformType,
} from '../../../shared/models';

@Injectable({ providedIn: 'root' })
export class ContentService {
  private readonly api = inject(ApiService);

  getAll(params?: { contentType?: ContentType; status?: ContentStatus; pageSize?: number; cursor?: string }): Observable<PagedResult<Content>> {
    let httpParams = new HttpParams();
    if (params?.contentType) httpParams = httpParams.set('contentType', params.contentType);
    if (params?.status) httpParams = httpParams.set('status', params.status);
    if (params?.pageSize) httpParams = httpParams.set('pageSize', params.pageSize.toString());
    if (params?.cursor) httpParams = httpParams.set('cursor', params.cursor);
    return this.api.get<PagedResult<Content>>('content', httpParams);
  }

  getById(id: string): Observable<Content> {
    return this.api.get<Content>(`content/${id}`);
  }

  create(request: CreateContentRequest): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('content', request);
  }

  update(request: UpdateContentRequest): Observable<void> {
    return this.api.put<void>(`content/${request.id}`, request);
  }

  remove(id: string): Observable<void> {
    return this.api.delete<void>(`content/${id}`);
  }

  // Content Pipeline
  createViaPipeline(request: ContentCreationRequest): Observable<Content> {
    return this.api.post<Content>('content-pipeline/create', request);
  }

  generateOutline(id: string): Observable<Content> {
    return this.api.post<Content>(`content-pipeline/${id}/outline`, {});
  }

  generateDraft(id: string): Observable<Content> {
    return this.api.post<Content>(`content-pipeline/${id}/draft`, {});
  }

  submitForReview(id: string): Observable<void> {
    return this.api.post<void>(`content-pipeline/${id}/submit`, {});
  }

  // Workflow
  transition(id: string, request: TransitionRequest): Observable<void> {
    return this.api.post<void>(`workflow/${id}/transition`, request);
  }

  getAllowedTransitions(id: string): Observable<ContentStatus[]> {
    return this.api.get<ContentStatus[]>(`workflow/${id}/transitions`);
  }

  getAuditLog(contentId: string, pageSize = 50): Observable<WorkflowTransitionLog[]> {
    const params = new HttpParams().set('contentId', contentId).set('pageSize', pageSize.toString());
    return this.api.get<WorkflowTransitionLog[]>('workflow/audit', params);
  }

  // Approval
  getPendingApproval(pageSize = 50): Observable<Content[]> {
    const params = new HttpParams().set('pageSize', pageSize.toString());
    return this.api.get<Content[]>('approval/pending', params);
  }

  approve(id: string): Observable<void> {
    return this.api.post<void>(`approval/${id}/approve`, {});
  }

  reject(id: string, feedback: string): Observable<void> {
    return this.api.post<void>(`approval/${id}/reject`, { feedback });
  }

  // Brand Voice
  getBrandVoiceScore(contentId: string): Observable<BrandVoiceScore> {
    return this.api.get<BrandVoiceScore>(`brand-voice/score/${contentId}`);
  }

  // Scheduling
  schedule(id: string, scheduledAt: string): Observable<void> {
    return this.api.post<void>(`scheduling/${id}/schedule`, { scheduledAt });
  }

  reschedule(id: string, scheduledAt: string): Observable<void> {
    return this.api.put<void>(`scheduling/${id}/reschedule`, { scheduledAt });
  }

  cancelSchedule(id: string): Observable<void> {
    return this.api.delete<void>(`scheduling/${id}`);
  }

  // Repurposing
  repurpose(id: string, targetPlatforms: readonly PlatformType[]): Observable<void> {
    return this.api.post<void>(`repurposing/${id}/repurpose`, { targetPlatforms });
  }

  getRepurposeSuggestions(id: string): Observable<RepurposingSuggestion[]> {
    return this.api.get<RepurposingSuggestion[]>(`repurposing/${id}/repurpose-suggestions`);
  }
}
