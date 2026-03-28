import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  EngagementTask,
  EngagementExecution,
  EngagementAction,
  CreateEngagementTaskRequest,
  UpdateEngagementTaskRequest,
  SocialInboxItem,
  SocialPlatformType,
  DiscoveredOpportunity,
  EngageSingleRequest,
  SocialStats,
  SafetyStatus,
} from '../models/social.model';

@Injectable({ providedIn: 'root' })
export class SocialService {
  private readonly api = inject(ApiService);

  // Stats & Safety
  getStats(): Observable<SocialStats> {
    return this.api.get<SocialStats>('social/stats');
  }

  getSafetyStatus(): Observable<SafetyStatus> {
    return this.api.get<SafetyStatus>('social/safety-status');
  }

  // Engagement tasks
  getTasks(): Observable<EngagementTask[]> {
    return this.api.get<EngagementTask[]>('social/tasks');
  }

  createTask(request: CreateEngagementTaskRequest): Observable<EngagementTask> {
    return this.api.post<EngagementTask>('social/tasks', request);
  }

  updateTask(id: string, request: UpdateEngagementTaskRequest): Observable<void> {
    return this.api.put<void>(`social/tasks/${id}`, request);
  }

  deleteTask(id: string): Observable<void> {
    return this.api.delete<void>(`social/tasks/${id}`);
  }

  executeTask(id: string): Observable<EngagementExecution> {
    return this.api.post<EngagementExecution>(`social/tasks/${id}/execute`, {});
  }

  getHistory(taskId: string, limit = 20): Observable<EngagementExecution[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.api.get<EngagementExecution[]>(`social/tasks/${taskId}/history`, params);
  }

  // Opportunities
  discoverOpportunities(): Observable<DiscoveredOpportunity[]> {
    return this.api.post<DiscoveredOpportunity[]>('social/discover', {});
  }

  engageSingle(request: EngageSingleRequest): Observable<EngagementAction> {
    return this.api.post<EngagementAction>('social/engage', request);
  }

  dismissOpportunity(postUrl: string, platform: string): Observable<void> {
    return this.api.post<void>('social/opportunities/dismiss', { postUrl, platform });
  }

  saveOpportunity(postUrl: string, platform: string): Observable<void> {
    return this.api.post<void>('social/opportunities/save', { postUrl, platform });
  }

  getSavedOpportunities(): Observable<DiscoveredOpportunity[]> {
    return this.api.get<DiscoveredOpportunity[]>('social/opportunities/saved');
  }

  // Inbox
  getInboxItems(params?: { platform?: SocialPlatformType; isRead?: boolean; limit?: number }): Observable<SocialInboxItem[]> {
    let httpParams = new HttpParams();
    if (params?.platform) httpParams = httpParams.set('platform', params.platform);
    if (params?.isRead !== undefined) httpParams = httpParams.set('isRead', params.isRead.toString());
    if (params?.limit) httpParams = httpParams.set('limit', params.limit.toString());
    return this.api.get<SocialInboxItem[]>('social/inbox', httpParams);
  }

  markRead(id: string): Observable<void> {
    return this.api.put<void>(`social/inbox/${id}/read`, {});
  }

  draftReply(id: string): Observable<string> {
    return this.api.post<string>(`social/inbox/${id}/draft`, {});
  }

  sendReply(id: string, replyText: string): Observable<void> {
    return this.api.post<void>(`social/inbox/${id}/reply`, { replyText });
  }
}
