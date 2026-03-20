import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { Platform, AuthUrlResponse, OAuthCallbackRequest, TestPostRequest, PlatformType } from '../../../shared/models';

@Injectable({ providedIn: 'root' })
export class PlatformService {
  private readonly api = inject(ApiService);

  getAll(): Observable<Platform[]> {
    return this.api.get<Platform[]>('platforms');
  }

  getAuthUrl(type: PlatformType): Observable<AuthUrlResponse> {
    return this.api.get<AuthUrlResponse>(`platforms/${type}/auth-url`);
  }

  handleCallback(type: PlatformType, request: OAuthCallbackRequest): Observable<void> {
    return this.api.post<void>(`platforms/${type}/callback`, request);
  }

  disconnect(type: PlatformType): Observable<void> {
    return this.api.delete<void>(`platforms/${type}/disconnect`);
  }

  getStatus(type: PlatformType): Observable<Platform> {
    return this.api.get<Platform>(`platforms/${type}/status`);
  }

  testPost(type: PlatformType, request: TestPostRequest): Observable<void> {
    return this.api.post<void>(`platforms/${type}/test-post`, request);
  }
}
