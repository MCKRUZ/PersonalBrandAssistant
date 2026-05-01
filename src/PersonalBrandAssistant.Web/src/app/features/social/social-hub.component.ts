import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TabsModule } from 'primeng/tabs';
import { BadgeModule } from 'primeng/badge';
import { SocialStore } from './store/social.store';
import { EngagementTaskListComponent } from './components/engagement-task-list.component';
import { OpportunitiesListComponent } from './components/opportunities-list.component';
import { InboxListComponent } from './components/inbox-list.component';

@Component({
  selector: 'app-social-hub',
  standalone: true,
  imports: [
    CommonModule, TabsModule, BadgeModule,
    EngagementTaskListComponent, OpportunitiesListComponent, InboxListComponent,
  ],
  template: `
    <div class="social-hub">
      <div class="page-header">
        <span class="breadcrumb">ENGAGEMENT</span>
        <h1>Social</h1>
        <p class="subtitle">Community engagement, publishing, and unified inbox</p>
      </div>

      <p-tabs [value]="store.activeTab()" (valueChange)="onTabChange($any($event))">
        <p-tablist>
          <p-tab value="automation">
            <i class="pi pi-cog mr-2"></i>
            Automation
          </p-tab>
          <p-tab value="opportunities">
            <i class="pi pi-compass mr-2"></i>
            Opportunities
          </p-tab>
          <p-tab value="inbox">
            <i class="pi pi-inbox mr-2"></i>
            Inbox
            @if (store.unreadCount() > 0) {
              <p-badge [value]="store.unreadCount().toString()" severity="danger" class="ml-2" />
            }
          </p-tab>
        </p-tablist>
        <p-tabpanels>
          <p-tabpanel value="automation">
            <app-engagement-task-list />
          </p-tabpanel>
          <p-tabpanel value="opportunities">
            <app-opportunities-list />
          </p-tabpanel>
          <p-tabpanel value="inbox">
            <app-inbox-list />
          </p-tabpanel>
        </p-tabpanels>
      </p-tabs>
    </div>
  `,
  styles: [`
    .social-hub { padding: 24px 32px 60px; }
    .page-header { margin-bottom: 1.5rem; }
    .breadcrumb {
      font-family: var(--font-mono, 'JetBrains Mono', monospace);
      font-size: 11px;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.1em;
      margin-bottom: 0.25rem;
      display: block;
    }
    .page-header h1 {
      margin: 0 0 0.25rem 0;
      font-size: 1.5rem;
      font-weight: 600;
      color: var(--text-color);
    }
    .subtitle {
      color: var(--text-color-secondary);
      margin: 0;
      font-size: 0.9rem;
    }
    .mr-2 { margin-right: 0.5rem; }
    .ml-2 { margin-left: 0.5rem; }
  `],
})
export class SocialHubComponent implements OnInit {
  readonly store = inject(SocialStore);

  ngOnInit() {
    this.store.loadTasks();
    this.store.loadInbox({});
    this.store.loadStats();
    this.store.loadSafetyStatus();
  }

  onTabChange(tab: string) {
    this.store.setActiveTab(tab as 'automation' | 'opportunities' | 'inbox');
  }
}
