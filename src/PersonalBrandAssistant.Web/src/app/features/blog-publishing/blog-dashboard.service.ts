import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { BlogPipelineItem, DashboardFilter } from './blog-dashboard.models';

@Injectable({ providedIn: 'root' })
export class BlogDashboardService {
  private readonly api = inject(ApiService);

  getItems(filter?: DashboardFilter): Observable<BlogPipelineItem[]> {
    let params = new HttpParams();
    if (filter?.status) params = params.set('status', filter.status);
    if (filter?.from) params = params.set('from', filter.from);
    if (filter?.to) params = params.set('to', filter.to);
    return this.api.get<BlogPipelineItem[]>('blog-pipeline', params);
  }

  schedule(contentId: string): Observable<{ scheduledAt: string }> {
    return this.api.post<{ scheduledAt: string }>(`blog-pipeline/${contentId}/schedule`, {});
  }

  updateDelay(contentId: string, delayDays: number | null): Observable<{ blogDelayDays: number | null }> {
    return this.api.put<{ blogDelayDays: number | null }>(`blog-pipeline/${contentId}/delay`, { delayDays });
  }

  skipBlog(contentId: string): Observable<{ blogSkipped: boolean }> {
    return this.api.post<{ blogSkipped: boolean }>(`blog-pipeline/${contentId}/skip-blog`, {});
  }
}
