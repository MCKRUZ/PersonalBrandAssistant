import { Component, inject, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { PlatformCardComponent } from './components/platform-card.component';
import { PlatformDetailDialogComponent } from './components/platform-detail-dialog.component';
import { TestPostDialogComponent } from './components/test-post-dialog.component';
import { PlatformStore } from './store/platform.store';
import { PlatformService } from './services/platform.service';
import { Platform } from '../../shared/models';

@Component({
  selector: 'app-platforms-list',
  standalone: true,
  imports: [
    CommonModule, ConfirmDialog, PageHeaderComponent, LoadingSpinnerComponent,
    EmptyStateComponent, PlatformCardComponent, PlatformDetailDialogComponent,
    TestPostDialogComponent,
  ],
  providers: [ConfirmationService],
  template: `
    <p-confirmDialog />
    <app-platform-detail-dialog #detailDialog />
    <app-test-post-dialog #testDialog />

    <app-page-header title="Platforms" />

    @if (store.loading()) {
      <app-loading-spinner message="Loading platforms..." />
    } @else if (store.platforms().length === 0) {
      <app-empty-state message="No platforms configured" icon="pi pi-share-alt" />
    } @else {
      <div class="grid">
        @for (platform of store.platforms(); track platform.id) {
          <div class="col-12 md:col-6">
            <app-platform-card
              [platform]="platform"
              (connect)="connectPlatform(platform)"
              (disconnect)="disconnectPlatform(platform)"
              (details)="detailDialog.open(platform)"
              (testPost)="testDialog.open(platform.type)"
            />
          </div>
        }
      </div>
    }
  `,
})
export class PlatformsListComponent implements OnInit {
  readonly store = inject(PlatformStore);
  private readonly platformService = inject(PlatformService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);

  @ViewChild('detailDialog') detailDialog!: PlatformDetailDialogComponent;
  @ViewChild('testDialog') testDialog!: TestPostDialogComponent;

  ngOnInit() {
    this.store.loadPlatforms(undefined);
  }

  connectPlatform(platform: Platform) {
    this.store.setConnecting(true);
    this.platformService.getAuthUrl(platform.type).subscribe({
      next: response => {
        this.store.setConnecting(false);
        window.location.href = response.authUrl;
      },
      error: () => this.store.setConnecting(false),
    });
  }

  disconnectPlatform(platform: Platform) {
    this.confirmationService.confirm({
      message: `Disconnect ${platform.displayName}?`,
      accept: () => {
        this.platformService.disconnect(platform.type).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Disconnected' });
            this.store.loadPlatforms(undefined);
          },
        });
      },
    });
  }
}
