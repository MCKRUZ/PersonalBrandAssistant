import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AutomationRun, AutomationConfig, TriggerResult } from '../../../shared/models';
import { HttpParams } from '@angular/common/http';

@Injectable({ providedIn: 'root' })
export class AutomationService {
  private readonly api = inject(ApiService);

  getRuns(limit = 20): Observable<AutomationRun[]> {
    const params = new HttpParams().set('limit', limit);
    return this.api.get<AutomationRun[]>('automation/runs', params);
  }

  getRun(id: string): Observable<AutomationRun> {
    return this.api.get<AutomationRun>(`automation/runs/${id}`);
  }

  trigger(): Observable<TriggerResult> {
    return this.api.post<TriggerResult>('automation/trigger', {});
  }

  getConfig(): Observable<AutomationConfig> {
    return this.api.get<AutomationConfig>('automation/config');
  }

  deleteRun(id: string): Observable<void> {
    return this.api.delete<void>(`automation/runs/${id}`);
  }

  clearRuns(): Observable<{ deleted: number }> {
    return this.api.delete<{ deleted: number }>('automation/runs');
  }
}
