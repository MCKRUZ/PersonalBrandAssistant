import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { BlogPipelineItem, BlogPipelineStage } from '../models/blog-pipeline.model';

@Injectable({ providedIn: 'root' })
export class BlogPipelineService {
  private readonly api = inject(ApiService);

  getAll(): Observable<BlogPipelineItem[]> {
    return this.api.get<BlogPipelineItem[]>('blog-pipeline');
  }

  advanceStage(contentId: string, note?: string): Observable<{ currentBlogStage: BlogPipelineStage }> {
    return this.api.put(`blog-pipeline/${contentId}/advance`, { note: note ?? null });
  }

  setStage(contentId: string, stage: BlogPipelineStage, note?: string): Observable<{ currentBlogStage: BlogPipelineStage }> {
    return this.api.put(`blog-pipeline/${contentId}/stage`, { stage, note: note ?? null });
  }
}
