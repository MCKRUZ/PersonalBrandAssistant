import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { CalendarSlot, CalendarSlotRequest, ContentSeries, ContentSeriesRequest } from '../../shared/models';

@Injectable()
export class CalendarApiService {
  private readonly api = inject(ApiService);

  getSlots(from: string, to: string): Observable<CalendarSlot[]> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.api.get<CalendarSlot[]>('calendar', params);
  }

  createSlot(request: CalendarSlotRequest): Observable<CalendarSlot> {
    return this.api.post<CalendarSlot>('calendar/slot', request);
  }

  assignContent(slotId: string, contentId: string): Observable<void> {
    return this.api.put<void>(`calendar/slot/${slotId}/assign`, { contentId });
  }

  autoFill(from: string, to: string): Observable<void> {
    return this.api.post<void>('calendar/auto-fill', { from, to });
  }

  createSeries(request: ContentSeriesRequest): Observable<ContentSeries> {
    return this.api.post<ContentSeries>('calendar/series', request);
  }
}
