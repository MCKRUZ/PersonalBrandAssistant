import { Injectable, inject } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { ChatMessage, FinalizedDraft } from '../models/blog-chat.models';

@Injectable({ providedIn: 'root' })
export class BlogChatService {
  private readonly api = inject(ApiService);

  getHistory(contentId: string): Observable<ChatMessage[]> {
    return this.api.get<ChatMessage[]>(`content/${contentId}/chat/history`);
  }

  sendMessage(contentId: string, message: string): Observable<string> {
    const subject = new Subject<string>();

    fetch(`/api/content/${contentId}/chat`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message }),
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

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          if (line.startsWith('data: ')) {
            const data = line.slice(6);
            if (data === '[DONE]') {
              subject.complete();
              return;
            }
            subject.next(data);
          } else if (line.startsWith('event: error')) {
            subject.error(new Error('Stream error'));
            return;
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
