import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Digest, DigestKind, DigestSummary } from '../models/digest.model';

@Injectable({ providedIn: 'root' })
export class DigestService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/digests';

  getLatest(kind: DigestKind = 'main'): Observable<Digest> {
    return this.http.get<Digest>(`${this.baseUrl}/latest`, { params: { kind } });
  }

  /** Fetch a specific date's brief for a kind — used to load the Microsoft brief beside the selected Main one. */
  getByDate(date: string, kind: DigestKind): Observable<Digest> {
    return this.http.get<Digest>(`${this.baseUrl}/by-date/${date}`, { params: { kind } });
  }

  getById(id: string): Observable<Digest> {
    return this.http.get<Digest>(`${this.baseUrl}/${id}`);
  }

  list(): Observable<DigestSummary[]> {
    return this.http.get<DigestSummary[]>(this.baseUrl);
  }
}
