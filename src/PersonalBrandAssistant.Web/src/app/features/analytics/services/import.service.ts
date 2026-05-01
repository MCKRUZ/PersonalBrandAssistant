import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';

export interface ImportSocialPostRequest {
  readonly platform: string;
  readonly platformPostId: string;
  readonly postUrl?: string;
  readonly title?: string;
  readonly body?: string;
  readonly publishedAt?: string;
}

export interface ImportSocialPostResponse {
  readonly contentId: string;
  readonly contentPlatformStatusId: string;
}

export interface BulkImportResponse {
  readonly imported: number;
  readonly total: number;
  readonly results: readonly Record<string, unknown>[];
}

@Injectable({ providedIn: 'root' })
export class ImportService {
  private readonly api = inject(ApiService);

  importSocialPost(request: ImportSocialPostRequest): Observable<ImportSocialPostResponse> {
    return this.api.post<ImportSocialPostResponse>('import/social-post', request);
  }

  importSocialPostsBulk(requests: ImportSocialPostRequest[]): Observable<BulkImportResponse> {
    return this.api.post<BulkImportResponse>('import/social-posts/bulk', requests);
  }
}
