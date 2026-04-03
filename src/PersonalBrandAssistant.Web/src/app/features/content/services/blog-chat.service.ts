import { Injectable, inject } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { environment } from '../../../environments/environment';
import { ChatMessage, FinalizedDraft } from '../models/blog-chat.models';

@Injectable({ providedIn: 'root' })
export class BlogChatService {
  private readonly api = inject(ApiService);

  getHistory(contentId: string): Observable<ChatMessage[]> {
    return this.api.get<ChatMessage[]>(`content/${contentId}/chat/history`);
  }

  sendMessage(contentId: string, message: string): Observable<string> {
    const subject = new Subject<string>();
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 600_000);

    // Clean up timeout on completion or error
    subject.subscribe({ complete: () => clearTimeout(timeoutId), error: () => clearTimeout(timeoutId) });

    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (environment.apiKey) {
      headers['X-Api-Key'] = environment.apiKey;
    }

    fetch(`${environment.apiUrl}/content/${contentId}/chat`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ message }),
      signal: controller.signal,
    }).then(async (response) => {
      if (!response.ok) {
        subject.error(new Error(`Chat request failed: ${response.status}`));
        return;
      }

      const reader = response.body?.getReader();
      if (!reader) {
        subject.error(new Error('No response body'));
        return;
      }

      const decoder = new TextDecoder();
      let buffer = '';
      let currentEvent = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          if (line.startsWith('event: ')) {
            currentEvent = line.slice(7).trim();
            if (currentEvent === 'error') {
              subject.error(new Error('Stream error'));
              return;
            }
            if (currentEvent === 'done') {
              subject.complete();
              return;
            }
          } else if (line.startsWith('data: ')) {
            const raw = line.slice(6);
            if (raw === '[DONE]') {
              subject.complete();
              return;
            }
            try {
              const parsed = JSON.parse(raw);
              if (parsed.text) {
                subject.next(parsed.text);
              }
            } catch {
              subject.next(raw);
            }
          }
        }
      }

      subject.complete();
    }).catch((err) => subject.error(err));

    return subject.asObservable();
  }

  finalize(contentId: string): Observable<FinalizedDraft> {
    return this.api.post<FinalizedDraft>(`content/${contentId}/chat/finalize`, {});
  }
}
