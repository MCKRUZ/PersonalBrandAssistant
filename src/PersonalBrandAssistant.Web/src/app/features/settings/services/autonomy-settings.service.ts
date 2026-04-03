import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AutonomySettings, UpdateAutonomySettingsRequest } from '../models/autonomy-settings.model';

@Injectable({ providedIn: 'root' })
export class AutonomySettingsService {
  private readonly api = inject(ApiService);

  getSettings(): Observable<AutonomySettings> {
    return this.api.get<AutonomySettings>('settings/autonomy');
  }

  updateSettings(request: UpdateAutonomySettingsRequest): Observable<AutonomySettings> {
    return this.api.put<AutonomySettings>('settings/autonomy', request);
  }
}
