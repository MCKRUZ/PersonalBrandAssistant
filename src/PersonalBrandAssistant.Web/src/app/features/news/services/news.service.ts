import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { IdeaService } from '../../../core/services/idea.service';
import { Idea, IdeaSource, IdeaStatus } from '../../../models/idea.model';

@Injectable({ providedIn: 'root' })
export class NewsService {
  private readonly ideaService = inject(IdeaService);

  getIdeas(limit = 5000): Observable<Idea[]> {
    return this.ideaService.list(
      { status: null, sourceId: null, category: null, tags: [], dateFrom: null, dateTo: null, searchText: null },
      1, limit,
      { field: 'detectedAt', direction: 'desc' }
    ).pipe(map(result => result.items));
  }

  getSavedIdeas(): Observable<Idea[]> {
    return this.ideaService.list(
      { status: IdeaStatus.Saved, sourceId: null, category: null, tags: [], dateFrom: null, dateTo: null, searchText: null },
      1, 1000,
      { field: 'detectedAt', direction: 'desc' }
    ).pipe(map(result => result.items));
  }

  saveIdea(id: string): Observable<void> {
    return this.ideaService.save(id, null, []);
  }

  dismissIdea(id: string): Observable<void> {
    return this.ideaService.dismiss(id);
  }

  refreshSources(): Observable<number> {
    return this.ideaService.refreshSources();
  }

  getSources(): Observable<IdeaSource[]> {
    return this.ideaService.listSources();
  }
}
