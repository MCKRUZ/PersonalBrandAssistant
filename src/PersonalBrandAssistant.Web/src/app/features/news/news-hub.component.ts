import { Component, computed, inject, signal, ViewEncapsulation } from '@angular/core';
import { TabsModule } from 'primeng/tabs';
import { ButtonModule } from 'primeng/button';
import { NewsFeedComponent } from './components/feed/news-feed.component';
import { HotTopicsGridComponent } from './components/hot-topics/hot-topics-grid.component';
import { ContentOpportunitiesComponent } from './components/opportunities/content-opportunities.component';
import { SavedItemsComponent } from './components/saved/saved-items.component';
import { SavedItemsStore } from './store/saved-items.store';
import { NewsStore } from './store/news.store';

@Component({
  selector: 'app-news-hub',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [
    TabsModule,
    ButtonModule,
    NewsFeedComponent,
    HotTopicsGridComponent,
    ContentOpportunitiesComponent,
    SavedItemsComponent,
  ],
  template: `
    <div class="nhub">
      <!-- Hero -->
      <header class="nhub-hero">
        <div class="nhub-hero__content">
          <div class="nhub-badge">
            <span class="nhub-badge__dot"></span>
            <i class="pi pi-globe"></i>
            LIVE
          </div>
          <div class="nhub-hero__title">News Hub</div>
          <div class="nhub-hero__sub">Your single source of truth for news, trends & content opportunities</div>
        </div>
        <div class="nhub-hero__orb"></div>
      </header>

      <!-- Body -->
      <div class="nhub-body">
        <div class="nhub-body__main">
          <p-tabs [value]="activeTab()" (valueChange)="setTab($event)" styleClass="nhub-tabs">
            <p-tablist>
              <p-tab [value]="0">
                <i class="pi pi-list mr-2"></i>Feed
                @if (storyCount() > 0) {
                  <span class="nhub-tab-count">{{ storyCount() }}</span>
                }
              </p-tab>
              <p-tab [value]="1"><i class="pi pi-bolt mr-2"></i>Hot Topics</p-tab>
              <p-tab [value]="2"><i class="pi pi-lightbulb mr-2"></i>Opportunities</p-tab>
              <p-tab [value]="3"><i class="pi pi-bookmark mr-2"></i>Saved</p-tab>
              <!-- Refresh status indicator -->
              <div class="nhub-refresh-status">
                @if (newsStore.refreshing()) {
                  <i class="pi pi-spin pi-spinner nhub-refresh-status__icon"></i>
                  <span>Refreshing feeds...</span>
                } @else if (newsStore.lastRefreshDelta() > 0) {
                  <span class="nhub-refresh-status__delta">+{{ newsStore.lastRefreshDelta() }} new</span>
                }
              </div>
            </p-tablist>
            <p-tabpanels>
              <p-tabpanel [value]="0"><app-news-feed /></p-tabpanel>
              <p-tabpanel [value]="1"><app-hot-topics-grid /></p-tabpanel>
              <p-tabpanel [value]="2"><app-content-opportunities /></p-tabpanel>
              <p-tabpanel [value]="3"><app-saved-items /></p-tabpanel>
            </p-tabpanels>
          </p-tabs>
        </div>
      </div>
    </div>
  `,
  styles: [`
    /* ── ViewEncapsulation.None = global scope, no _ngcontent attr ── */

    .nhub {
      padding: 0 2rem 2rem;
      min-height: calc(100vh - 60px);
      background: var(--p-surface-950, #09090b);
    }

    /* ── Hero ── */
    .nhub-hero {
      position: relative;
      padding: 2.25rem 0 1.5rem;
      overflow: hidden;
    }
    .nhub-hero__content { position: relative; z-index: 1; }
    .nhub-hero__title {
      font-size: 2.25rem;
      font-weight: 800;
      letter-spacing: -0.03em;
      line-height: 1.15;
      color: #f4f4f5 !important;
    }
    .nhub-hero__sub {
      margin-top: 0.4rem;
      font-size: 0.9rem;
      color: #71717a;
    }
    .nhub-hero__orb {
      position: absolute;
      top: -80px; right: -60px;
      width: 300px; height: 300px;
      border-radius: 50%;
      background: radial-gradient(circle, rgba(139,92,246,0.18) 0%, transparent 70%);
      filter: blur(60px);
      pointer-events: none;
    }

    /* ── Badge ── */
    .nhub-badge {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.3rem 0.85rem;
      border-radius: 2rem;
      background: rgba(139,92,246,0.1);
      border: 1px solid rgba(139,92,246,0.25);
      font-size: 0.68rem;
      font-weight: 700;
      letter-spacing: 0.12em;
      text-transform: uppercase;
      color: #a78bfa;
      margin-bottom: 0.75rem;
    }
    .nhub-badge i { font-size: 0.6rem; }
    .nhub-badge__dot {
      width: 7px; height: 7px;
      border-radius: 50%;
      background: #22c55e;
      box-shadow: 0 0 8px rgba(34,197,94,0.5);
      animation: nhub-pulse 2s ease-in-out infinite;
    }

    /* ── Body Layout ── */
    .nhub-body {
      display: flex;
      gap: 1.5rem;
    }
    .nhub-body__main {
      flex: 1;
      min-width: 0;
    }

    /* ── Tab Overrides ── */
    .nhub-tabs .p-tablist {
      border-bottom: 1px solid rgba(255,255,255,0.06) !important;
      background: transparent !important;
      position: relative;
    }

    /* ── Story count on Feed tab ── */
    .nhub-tab-count {
      margin-left: 0.4rem;
      font-size: 0.65rem;
      font-weight: 700;
      padding: 0.1rem 0.45rem;
      border-radius: 8px;
      background: rgba(139,92,246,0.15);
      color: #a78bfa;
    }

    /* ── Refresh status on tab bar ── */
    .nhub-refresh-status {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      margin-left: auto;
      padding: 0 1rem;
      font-size: 0.78rem;
      font-weight: 600;
      color: #71717a;
    }
    .nhub-refresh-status__icon {
      font-size: 0.8rem;
      color: #a78bfa;
    }
    .nhub-refresh-status__delta {
      color: #22c55e;
      animation: nhub-delta-fade 4s ease-out forwards;
    }
    @keyframes nhub-delta-fade {
      0% { opacity: 1; }
      70% { opacity: 1; }
      100% { opacity: 0; }
    }
    .nhub-tabs .p-tab {
      font-weight: 600 !important;
      font-size: 0.88rem !important;
      padding: 0.85rem 1.25rem !important;
      color: #71717a !important;
      background: transparent !important;
      border: none !important;
    }
    .nhub-tabs .p-tab:hover {
      color: #a1a1aa !important;
    }
    .nhub-tabs .p-tab[data-p-active="true"] {
      color: #a78bfa !important;
    }
    .nhub-tabs .p-tablist-active-bar {
      background: #8b5cf6 !important;
      box-shadow: 0 2px 12px rgba(139,92,246,0.35);
      height: 2.5px !important;
    }
    .nhub-tabs .p-tabpanels {
      padding: 1.5rem 0 0 !important;
      background: transparent !important;
    }
    .nhub-tabs .p-tabpanel {
      background: transparent !important;
      padding: 0 !important;
    }

    /* ── Animation ── */
    @keyframes nhub-pulse {
      0%, 100% { opacity: 1; transform: scale(1); }
      50% { opacity: 0.4; transform: scale(0.7); }
    }

    /* ── Responsive ── */
    @media (max-width: 768px) {
      .nhub { padding: 0 1rem 1.5rem; }
      .nhub-hero__orb { display: none; }
      .nhub-hero__title { font-size: 1.75rem; }
    }
    @media (prefers-reduced-motion: reduce) {
      .nhub-badge__dot { animation: none; }
    }
  `],
})
export class NewsHubComponent {
  private readonly savedItemsStore = inject(SavedItemsStore);
  readonly newsStore = inject(NewsStore);
  readonly activeTab = signal(0);
  readonly storyCount = computed(() => this.newsStore.filteredItems().length);

  setTab(event: string | number | undefined) {
    if (typeof event === 'number') {
      this.activeTab.set(event);
      if (event === 3) {
        this.savedItemsStore.load(undefined);
      }
    }
  }
}
