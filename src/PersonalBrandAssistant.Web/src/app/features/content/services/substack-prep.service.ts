import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { SubstackPreparedContent, SubstackPublishConfirmation } from '../models/substack-prep.models';

@Injectable({ providedIn: 'root' })
export class SubstackPrepService {
  private readonly api = inject(ApiService);

  getPrep(contentId: string): Observable<SubstackPreparedContent> {
    return this.api.get<SubstackPreparedContent>(`content/${contentId}/substack-prep`);
  }

  markPublished(contentId: string, substackUrl?: string): Observable<SubstackPublishConfirmation> {
    return this.api.post<SubstackPublishConfirmation>(
      `content/${contentId}/substack-published`,
      { substackUrl: substackUrl ?? null }
    );
  }
}
