import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Digest, DigestSummary } from '../models/digest.model';

@Injectable({ providedIn: 'root' })
export class DigestService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/digests';

  getLatest(): Observable<Digest> {
    return this.http.get<Digest>(`${this.baseUrl}/latest`);
  }

  getById(id: string): Observable<Digest> {
    return this.http.get<Digest>(`${this.baseUrl}/${id}`);
  }

  list(): Observable<DigestSummary[]> {
    return this.http.get<DigestSummary[]>(this.baseUrl);
  }
}
