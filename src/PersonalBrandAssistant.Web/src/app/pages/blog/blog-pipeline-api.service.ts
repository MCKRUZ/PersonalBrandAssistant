import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { BlogPipelineItem, BlogPipelineStage } from '../../features/blog-pipeline/models/blog-pipeline.model';

@Injectable({ providedIn: 'root' })
export class BlogPipelineApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  getById(contentId: string): Observable<BlogPipelineItem> {
    return this.http.get<BlogPipelineItem[]>(`${this.base}/blog-pipeline`).pipe(
      map(items => {
        const item = items.find(i => i.id === contentId);
        if (!item) throw new Error(`Pipeline item not found for content ${contentId}`);
        return item;
      }),
    );
  }

  advanceStage(contentId: string, note?: string): Observable<{ currentBlogStage: BlogPipelineStage }> {
    return this.http.put<{ currentBlogStage: BlogPipelineStage }>(
      `${this.base}/blog-pipeline/${contentId}/advance`,
      note ? { note } : {},
    );
  }

  setStage(contentId: string, stage: BlogPipelineStage, note?: string): Observable<{ currentBlogStage: BlogPipelineStage }> {
    return this.http.put<{ currentBlogStage: BlogPipelineStage }>(
      `${this.base}/blog-pipeline/${contentId}/stage`,
      { stage, note },
    );
  }

  confirmSchedule(contentId: string): Observable<{ scheduledAt: string }> {
    return this.http.post<{ scheduledAt: string }>(`${this.base}/blog-pipeline/${contentId}/schedule`, {});
  }

  updateDelay(contentId: string, delayDays: number | null): Observable<{ blogDelayDays: number | null }> {
    return this.http.put<{ blogDelayDays: number | null }>(
      `${this.base}/blog-pipeline/${contentId}/delay`,
      { delayDays },
    );
  }

  skipBlog(contentId: string): Observable<{ blogSkipped: boolean }> {
    return this.http.post<{ blogSkipped: boolean }>(`${this.base}/blog-pipeline/${contentId}/skip-blog`, {});
  }
}
