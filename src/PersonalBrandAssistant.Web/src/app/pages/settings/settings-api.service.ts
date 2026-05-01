import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AutonomySettings } from '../../core/models/autonomy.model';

@Injectable({ providedIn: 'root' })
export class SettingsApiService {
  private readonly http = inject(HttpClient);

  getAutonomy(): Observable<AutonomySettings> {
    return this.http.get<AutonomySettings>('/api/settings/autonomy');
  }

  updateAutonomy(settings: AutonomySettings): Observable<AutonomySettings> {
    return this.http.put<AutonomySettings>('/api/settings/autonomy', settings);
  }
}
