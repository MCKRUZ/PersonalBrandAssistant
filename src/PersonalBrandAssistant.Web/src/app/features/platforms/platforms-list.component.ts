import { Component } from '@angular/core';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-platforms-list',
  standalone: true,
  imports: [PageHeaderComponent, EmptyStateComponent],
  template: `
    <app-page-header title="Platforms" />
    <app-empty-state message="Platform integrations coming soon" icon="pi pi-share-alt" />
  `,
})
export class PlatformsListComponent {}
