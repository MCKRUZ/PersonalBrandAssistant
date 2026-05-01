import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ContentItem } from '../../core/models/content.model';

@Injectable()
export class ApprovalApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  getPending(pageSize = 50): Observable<ContentItem[]> {
    return this.http.get<ContentItem[]>(`${this.base}/approval/pending`, {
      params: { pageSize },
    });
  }

  approve(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/approval/${id}/approve`, {});
  }

  reject(id: string, feedback: string): Observable<void> {
    return this.http.post<void>(`${this.base}/approval/${id}/reject`, { feedback });
  }

  batchApprove(contentIds: readonly string[]): Observable<{ successCount: number }> {
    return this.http.post<{ successCount: number }>(`${this.base}/approval/batch-approve`, { contentIds });
  }
}
