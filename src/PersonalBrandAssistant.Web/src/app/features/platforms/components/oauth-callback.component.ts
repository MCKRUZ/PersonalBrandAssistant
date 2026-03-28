import { Component, inject, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';
import { PlatformService } from '../services/platform.service';
import { PlatformType } from '../../../shared/models';
import { MessageService } from 'primeng/api';

@Component({
  selector: 'app-oauth-callback',
  standalone: true,
  imports: [LoadingSpinnerComponent],
  template: `<app-loading-spinner message="Completing connection..." />`,
})
export class OAuthCallbackComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly platformService = inject(PlatformService);
  private readonly messageService = inject(MessageService);

  ngOnInit() {
    const queryParams = this.route.snapshot.queryParams;
    const code = queryParams['code'];
    const state = queryParams['state'];
    const platformType = this.route.snapshot.params['type'] as PlatformType
      ?? queryParams['platform'] as PlatformType;

    if (!code || !state || !platformType) {
      this.messageService.add({ severity: 'error', summary: 'Error', detail: 'Invalid OAuth callback' });
      this.router.navigate(['/platforms']);
      return;
    }

    this.platformService.handleCallback(platformType, { code, state }).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Connected', detail: `${platformType} connected successfully` });
        this.router.navigate(['/platforms']);
      },
      error: () => {
        this.router.navigate(['/platforms']);
      },
    });
  }
}
