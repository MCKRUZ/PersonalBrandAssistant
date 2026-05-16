import { Component } from '@angular/core';
import { QuickComposeWidgetComponent } from '../quick-compose-widget/quick-compose-widget.component';
import { TrendingTopicsWidgetComponent } from '../trending-topics-widget/trending-topics-widget.component';

@Component({
  selector: 'app-feed-sidebar',
  standalone: true,
  imports: [QuickComposeWidgetComponent, TrendingTopicsWidgetComponent],
  template: `
    <div class="sidebar">
      <app-quick-compose-widget />
      <app-trending-topics-widget />
    </div>
  `,
  styles: [`
    .sidebar {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }
  `]
})
export class FeedSidebarComponent {}
