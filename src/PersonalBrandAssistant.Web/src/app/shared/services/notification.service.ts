import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { Notification } from '../models';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly api = inject(ApiService);

  getAll(params?: { isRead?: boolean; pageSize?: number }): Observable<Notification[]> {
    let httpParams = new HttpParams();
    if (params?.isRead !== undefined) httpParams = httpParams.set('isRead', params.isRead.toString());
    if (params?.pageSize) httpParams = httpParams.set('pageSize', params.pageSize.toString());
    return this.api.get<Notification[]>('notifications', httpParams);
  }

  markRead(id: string): Observable<void> {
    return this.api.post<void>(`notifications/${id}/read`, {});
  }

  markAllRead(): Observable<void> {
    return this.api.post<void>('notifications/read-all', {});
  }
}
