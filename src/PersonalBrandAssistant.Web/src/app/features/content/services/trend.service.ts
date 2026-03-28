import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { TrendSuggestion } from '../../../shared/models';

@Injectable({ providedIn: 'root' })
export class TrendService {
  private readonly api = inject(ApiService);

  getSuggestions(limit = 50): Observable<TrendSuggestion[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.api.get<TrendSuggestion[]>('trends/suggestions', params);
  }

  accept(id: string): Observable<void> {
    return this.api.post<void>(`trends/suggestions/${id}/accept`, {});
  }

  dismiss(id: string): Observable<void> {
    return this.api.post<void>(`trends/suggestions/${id}/dismiss`, {});
  }

  refresh(): Observable<void> {
    return this.api.post<void>('trends/refresh', {});
  }
}
