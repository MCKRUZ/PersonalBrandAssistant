import { Component } from '@angular/core';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-calendar-view',
  standalone: true,
  imports: [PageHeaderComponent, EmptyStateComponent],
  template: `
    <app-page-header title="Calendar" />
    <app-empty-state message="Content calendar coming soon" icon="pi pi-calendar" />
  `,
})
export class CalendarViewComponent {}
