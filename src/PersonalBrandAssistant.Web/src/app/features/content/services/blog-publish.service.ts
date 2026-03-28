import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { BlogHtmlResult, BlogPublishResult, BlogDeployStatus } from '../models/blog-publish.models';

@Injectable({ providedIn: 'root' })
export class BlogPublishService {
  private readonly api = inject(ApiService);

  getPrep(contentId: string): Observable<BlogHtmlResult> {
    return this.api.get<BlogHtmlResult>(`content/${contentId}/blog-prep`);
  }

  publish(contentId: string): Observable<BlogPublishResult> {
    return this.api.post<BlogPublishResult>(`content/${contentId}/blog-publish`, {});
  }

  getStatus(contentId: string): Observable<BlogDeployStatus> {
    return this.api.get<BlogDeployStatus>(`content/${contentId}/blog-status`);
  }
}
