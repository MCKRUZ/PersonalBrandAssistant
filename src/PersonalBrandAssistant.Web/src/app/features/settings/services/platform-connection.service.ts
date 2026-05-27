import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  PlatformStatus,
  ConnectionStatusResponse,
  StoreCredentialsRequest,
} from '../models/platform-connection.model';

@Injectable({ providedIn: 'root' })
export class PlatformConnectionService {
  private readonly baseUrl = '/api';

  constructor(private readonly http: HttpClient) {}

  getPlatforms(): Observable<PlatformStatus[]> {
    return this.http.get<PlatformStatus[]>(`${this.baseUrl}/platforms`);
  }

  getStatus(platform: string): Observable<ConnectionStatusResponse> {
    return this.http.get<ConnectionStatusResponse>(
      `${this.baseUrl}/auth/${platform}/status`
    );
  }

  getAuthorizeUrl(platform: string): string {
    return `${this.baseUrl}/auth/${platform}/authorize`;
  }

  storeCredentials(
    platform: string,
    request: StoreCredentialsRequest
  ): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/platforms/${platform}/credentials`,
      request
    );
  }

  disconnect(platform: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/auth/${platform}`);
  }
}
