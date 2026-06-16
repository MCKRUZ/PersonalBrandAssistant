import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  Idea,
  IdeaDetail,
  IdeaConnection,
  IdeaSource,
  CreateIdeaRequest,
  IdeaSourceRequest,
  IdeaFilterState,
  IdeaSortState,
} from '../../models/idea.model';
import { PagedResult } from '../../models/pagination.model';
import {
  BrandRankingProfile,
  UpdateBrandProfileRequest,
} from '../../models/brand-profile.model';

@Injectable({ providedIn: 'root' })
export class IdeaService {
  private readonly baseUrl = '/api';
  private readonly brandProfileUrl = `${this.baseUrl}/brand-ranking-profile`;

  constructor(private readonly http: HttpClient) {}

  getBrandProfile(): Observable<BrandRankingProfile> {
    return this.http.get<BrandRankingProfile>(this.brandProfileUrl);
  }

  updateBrandProfile(request: UpdateBrandProfileRequest): Observable<BrandRankingProfile> {
    return this.http.put<BrandRankingProfile>(this.brandProfileUrl, request);
  }

  list(
    filter: Partial<IdeaFilterState>,
    page: number,
    pageSize: number,
    sort: IdeaSortState
  ): Observable<PagedResult<Idea>> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString())
      .set('sortBy', sort.field)
      .set('sortDirection', sort.direction);

    if (filter.status) params = params.set('status', filter.status);
    if (filter.sourceId) params = params.set('ideaSourceId', filter.sourceId);
    if (filter.category) params = params.set('category', filter.category);
    if (filter.searchText) params = params.set('searchText', filter.searchText);
    if (filter.dateFrom) params = params.set('dateFrom', filter.dateFrom);
    if (filter.dateTo) params = params.set('dateTo', filter.dateTo);
    if (filter.tags?.length) {
      filter.tags.forEach((tag) => {
        params = params.append('tags', tag);
      });
    }
    if (filter.minScore != null) params = params.set('minScore', filter.minScore.toString());

    return this.http.get<PagedResult<Idea>>(`${this.baseUrl}/ideas`, { params });
  }

  getById(id: string): Observable<IdeaDetail> {
    return this.http.get<IdeaDetail>(`${this.baseUrl}/ideas/${id}`);
  }

  create(request: CreateIdeaRequest): Observable<string> {
    return this.http.post<string>(`${this.baseUrl}/ideas`, request);
  }

  save(id: string, notes: string | null, tags: string[]): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/ideas/${id}/save`, { notes, tags });
  }

  dismiss(id: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/ideas/${id}/dismiss`, {});
  }

  createContent(id: string, contentType: string, platform: string): Observable<string> {
    return this.http.post<string>(`${this.baseUrl}/ideas/${id}/create-content`, {
      contentType,
      primaryPlatform: platform,
    });
  }

  getConnections(): Observable<IdeaConnection[]> {
    return this.http.get<IdeaConnection[]>(`${this.baseUrl}/ideas/connections`);
  }

  listSources(): Observable<IdeaSource[]> {
    return this.http.get<IdeaSource[]>(`${this.baseUrl}/idea-sources`);
  }

  createSource(source: IdeaSourceRequest): Observable<string> {
    return this.http.post<string>(`${this.baseUrl}/idea-sources`, source);
  }

  updateSource(id: string, source: Partial<IdeaSourceRequest>): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/idea-sources/${id}`, source);
  }

  deleteSource(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/idea-sources/${id}`);
  }

  refreshSources(): Observable<number> {
    return this.http.post<number>(`${this.baseUrl}/idea-sources/refresh`, {});
  }
}
