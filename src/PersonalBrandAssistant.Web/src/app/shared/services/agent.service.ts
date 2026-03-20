import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AgentExecuteRequest, AgentExecution, AgentUsage, AgentBudget } from '../models';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly api = inject(ApiService);

  execute(request: AgentExecuteRequest): Observable<{ executionId: string }> {
    return this.api.post<{ executionId: string }>('agents/execute', request);
  }

  getExecution(id: string): Observable<AgentExecution> {
    return this.api.get<AgentExecution>(`agents/executions/${id}`);
  }

  getExecutions(contentId?: string): Observable<AgentExecution[]> {
    let params = new HttpParams();
    if (contentId) params = params.set('contentId', contentId);
    return this.api.get<AgentExecution[]>('agents/executions', params);
  }

  getUsage(from: string, to: string): Observable<AgentUsage> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.api.get<AgentUsage>('agents/usage', params);
  }

  getBudget(): Observable<AgentBudget> {
    return this.api.get<AgentBudget>('agents/budget');
  }

  stream(request: AgentExecuteRequest): Observable<string> {
    return new Observable(subscriber => {
      const abortController = new AbortController();

      fetch(`${environment.apiUrl}/agents/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request),
        signal: abortController.signal,
      })
        .then(async response => {
          if (!response.ok || !response.body) {
            subscriber.error(new Error(`Stream failed: ${response.status}`));
            return;
          }
          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = '';

          while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            buffer = lines.pop() ?? '';
            for (const line of lines) {
              if (line.startsWith('data: ')) {
                subscriber.next(line.slice(6));
              }
            }
          }
          subscriber.complete();
        })
        .catch(err => {
          if (err.name !== 'AbortError') subscriber.error(err);
        });

      return () => abortController.abort();
    });
  }
}
