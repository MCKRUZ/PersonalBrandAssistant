diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
index fd874d2..d4f8b97 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts
@@ -11,6 +11,9 @@ import { DashboardKpiCardsComponent } from './components/dashboard-kpi-cards.com
 import { DateRangeSelectorComponent } from './components/date-range-selector.component';
 import { EngagementTimelineChartComponent } from './components/engagement-timeline-chart.component';
 import { PlatformBreakdownChartComponent } from './components/platform-breakdown-chart.component';
+import { PlatformHealthCardsComponent } from './components/platform-health-cards.component';
+import { WebsiteAnalyticsSectionComponent } from './components/website-analytics-section.component';
+import { SubstackSectionComponent } from './components/substack-section.component';
 import { AnalyticsStore } from './store/analytics.store';
 import { DashboardPeriod } from './models/dashboard.model';
 
@@ -22,6 +25,7 @@ import { DashboardPeriod } from './models/dashboard.model';
     PageHeaderComponent, EmptyStateComponent,
     TopContentTableComponent, DashboardKpiCardsComponent, DateRangeSelectorComponent,
     EngagementTimelineChartComponent, PlatformBreakdownChartComponent,
+    PlatformHealthCardsComponent, WebsiteAnalyticsSectionComponent, SubstackSectionComponent,
   ],
   changeDetection: ChangeDetectionStrategy.OnPush,
   template: `
@@ -65,9 +69,18 @@ import { DashboardPeriod } from './models/dashboard.model';
         <app-platform-breakdown-chart [timeline]="store.timeline()" />
       </div>
 
+      <div class="mt-3">
+        <app-platform-health-cards [platforms]="store.platformSummaries()" />
+      </div>
+
       <div class="mt-3">
         <app-top-content-table [items]="store.topContent()" (viewDetail)="viewDetail($event)" />
       </div>
+
+      <div class="bottom-row mt-3">
+        <app-website-analytics-section [data]="store.websiteData()" />
+        <app-substack-section [posts]="store.substackPosts()" />
+      </div>
     }
   `,
   styles: `
@@ -102,6 +115,14 @@ import { DashboardPeriod } from './models/dashboard.model';
       grid-template-columns: 2fr 1fr;
       gap: 1rem;
     }
+    .bottom-row {
+      display: grid;
+      grid-template-columns: 3fr 1fr;
+      gap: 1rem;
+    }
+    @media (max-width: 1024px) {
+      .bottom-row { grid-template-columns: 1fr; }
+    }
   `,
 })
 export class AnalyticsDashboardComponent implements OnInit {
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.spec.ts
new file mode 100644
index 0000000..fb17678
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.spec.ts
@@ -0,0 +1,98 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { PlatformHealthCardsComponent } from './platform-health-cards.component';
+import { PlatformSummary } from '../models/dashboard.model';
+
+describe('PlatformHealthCardsComponent', () => {
+  let component: PlatformHealthCardsComponent;
+  let fixture: ComponentFixture<PlatformHealthCardsComponent>;
+
+  const mockPlatforms: readonly PlatformSummary[] = [
+    { platform: 'TwitterX', followerCount: 2841, postCount: 45, avgEngagement: 128, topPostTitle: 'Why agent frameworks need a rethink', topPostUrl: 'https://x.com/post/1', isAvailable: true },
+    { platform: 'LinkedIn', followerCount: 5200, postCount: 0, avgEngagement: 0, topPostTitle: null, topPostUrl: null, isAvailable: false },
+    { platform: 'YouTube', followerCount: 1200, postCount: 18, avgEngagement: 3400, topPostTitle: 'Building AI Agents', topPostUrl: 'https://youtube.com/watch?v=1', isAvailable: true },
+    { platform: 'Instagram', followerCount: 890, postCount: 32, avgEngagement: 95, topPostTitle: 'AI at EY', topPostUrl: 'https://instagram.com/p/1', isAvailable: true },
+    { platform: 'Reddit', followerCount: null, postCount: 12, avgEngagement: 42, topPostTitle: 'Claude Code tips', topPostUrl: 'https://reddit.com/r/1', isAvailable: true },
+  ];
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [PlatformHealthCardsComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(PlatformHealthCardsComponent);
+    component = fixture.componentInstance;
+  });
+
+  it('should render a card for each platform in the input array', () => {
+    fixture.componentRef.setInput('platforms', mockPlatforms);
+    fixture.detectChanges();
+
+    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
+    expect(cards.length).toBe(5);
+  });
+
+  it('should display platform brand color as the top border', () => {
+    fixture.componentRef.setInput('platforms', mockPlatforms);
+    fixture.detectChanges();
+
+    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
+    const twitterCard = cards[0] as HTMLElement;
+    expect(twitterCard.style.getPropertyValue('--platform-color')).toBe('#1DA1F2');
+  });
+
+  it('should show follower count when available', () => {
+    fixture.componentRef.setInput('platforms', mockPlatforms);
+    fixture.detectChanges();
+
+    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
+    const twitterCard = cards[0] as HTMLElement;
+    expect(twitterCard.textContent).toContain('2,841');
+  });
+
+  it('should show N/A when followerCount is null', () => {
+    fixture.componentRef.setInput('platforms', mockPlatforms);
+    fixture.detectChanges();
+
+    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
+    const redditCard = cards[4] as HTMLElement;
+    expect(redditCard.textContent).toContain('N/A');
+  });
+
+  it('should show post count and average engagement', () => {
+    fixture.componentRef.setInput('platforms', mockPlatforms);
+    fixture.detectChanges();
+
+    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
+    const twitterCard = cards[0] as HTMLElement;
+    expect(twitterCard.textContent).toContain('45');
+    expect(twitterCard.textContent).toContain('128');
+  });
+
+  it('should display top post title when present', () => {
+    fixture.componentRef.setInput('platforms', mockPlatforms);
+    fixture.detectChanges();
+
+    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
+    const twitterCard = cards[0] as HTMLElement;
+    expect(twitterCard.textContent).toContain('Why agent frameworks need a rethink');
+  });
+
+  it('should show Coming Soon badge for LinkedIn', () => {
+    fixture.componentRef.setInput('platforms', mockPlatforms);
+    fixture.detectChanges();
+
+    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
+    const linkedinCard = cards[1] as HTMLElement;
+    expect(linkedinCard.textContent).toContain('Coming Soon');
+    expect(linkedinCard.querySelector('.unavailable-overlay')).toBeTruthy();
+  });
+
+  it('should show Data unavailable for non-LinkedIn unavailable platforms', () => {
+    const unavailableReddit: PlatformSummary = { platform: 'Reddit', followerCount: null, postCount: 0, avgEngagement: 0, topPostTitle: null, topPostUrl: null, isAvailable: false };
+    fixture.componentRef.setInput('platforms', [unavailableReddit]);
+    fixture.detectChanges();
+
+    const card = fixture.nativeElement.querySelector('.platform-card') as HTMLElement;
+    expect(card.textContent).toContain('Data unavailable');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.ts
new file mode 100644
index 0000000..b1a3733
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.ts
@@ -0,0 +1,202 @@
+import { ChangeDetectionStrategy, Component, input } from '@angular/core';
+import { CommonModule } from '@angular/common';
+import { Tag } from 'primeng/tag';
+import { PlatformSummary } from '../models/dashboard.model';
+import { PLATFORM_COLORS, PLATFORM_ICONS, PLATFORM_LABELS } from '../../../shared/utils/platform-icons';
+
+function followerLabel(platform: string): string {
+  if (platform === 'YouTube') return 'Subscribers';
+  if (platform === 'Reddit') return 'Karma';
+  return 'Followers';
+}
+
+function engagementLabel(platform: string): string {
+  if (platform === 'YouTube') return 'Avg Views';
+  if (platform === 'Reddit') return 'Avg Score';
+  return 'Avg Eng.';
+}
+
+function unavailableMessage(platform: string): string {
+  return platform === 'LinkedIn' ? 'Coming Soon' : 'Data unavailable';
+}
+
+@Component({
+  selector: 'app-platform-health-cards',
+  standalone: true,
+  imports: [CommonModule, Tag],
+  changeDetection: ChangeDetectionStrategy.OnPush,
+  template: `
+    <div class="platform-grid">
+      @for (p of platforms(); track p.platform) {
+        <div class="platform-card" [style.--platform-color]="getColor(p.platform)">
+          <div class="platform-header">
+            <i [class]="getIcon(p.platform)" class="platform-icon"></i>
+            <span class="platform-name">{{ getLabel(p.platform) }}</span>
+          </div>
+
+          @if (!p.isAvailable) {
+            <div class="unavailable-overlay">
+              <p-tag [value]="getUnavailableMessage(p.platform)" severity="warn" />
+            </div>
+          } @else {
+            <div class="stat-rows">
+              <div class="stat-row">
+                <span class="stat-label">{{ getFollowerLabel(p.platform) }}</span>
+                <span class="stat-value">{{ p.followerCount !== null ? (p.followerCount | number) : 'N/A' }}</span>
+              </div>
+              <div class="stat-row">
+                <span class="stat-label">Posts</span>
+                <span class="stat-value">{{ p.postCount | number }}</span>
+              </div>
+              <div class="stat-row">
+                <span class="stat-label">{{ getEngagementLabel(p.platform) }}</span>
+                <span class="stat-value">{{ p.avgEngagement | number }}</span>
+              </div>
+            </div>
+
+            @if (p.topPostTitle) {
+              <div class="top-post">
+                <span class="top-post-label">Top Post</span>
+                <span class="top-post-title">{{ p.topPostTitle }}</span>
+              </div>
+            }
+          }
+        </div>
+      }
+    </div>
+  `,
+  styles: `
+    .platform-grid {
+      display: grid;
+      grid-template-columns: repeat(5, 1fr);
+      gap: 1rem;
+    }
+    @media (max-width: 1200px) {
+      .platform-grid { grid-template-columns: repeat(3, 1fr); }
+    }
+    @media (max-width: 900px) {
+      .platform-grid { grid-template-columns: repeat(2, 1fr); }
+    }
+    @media (max-width: 600px) {
+      .platform-grid { grid-template-columns: 1fr; }
+    }
+
+    .platform-card {
+      position: relative;
+      overflow: hidden;
+      background: var(--p-surface-900, #111118);
+      border: 1px solid var(--p-surface-700, #25252f);
+      border-radius: 12px;
+      padding: 1rem 1.1rem;
+      transition: border-color 0.2s ease, transform 0.2s ease;
+    }
+    .platform-card::before {
+      content: '';
+      position: absolute;
+      top: 0;
+      left: 0;
+      right: 0;
+      height: 3px;
+      background: var(--platform-color);
+    }
+    .platform-card:hover {
+      border-color: var(--p-surface-600, #3a3a48);
+      transform: translateY(-1px);
+    }
+
+    .platform-header {
+      display: flex;
+      align-items: center;
+      gap: 0.5rem;
+      margin-bottom: 0.75rem;
+    }
+    .platform-icon {
+      font-size: 1rem;
+      color: var(--platform-color);
+    }
+    .platform-name {
+      font-size: 0.8rem;
+      font-weight: 700;
+      text-transform: uppercase;
+      letter-spacing: 0.04em;
+      color: var(--p-text-muted-color, #71717a);
+    }
+
+    .stat-rows {
+      display: flex;
+      flex-direction: column;
+      gap: 0.4rem;
+    }
+    .stat-row {
+      display: flex;
+      justify-content: space-between;
+      align-items: center;
+    }
+    .stat-label {
+      font-size: 0.75rem;
+      color: var(--p-text-muted-color, #71717a);
+    }
+    .stat-value {
+      font-size: 0.85rem;
+      font-weight: 700;
+    }
+
+    .top-post {
+      margin-top: 0.6rem;
+      padding-top: 0.6rem;
+      border-top: 1px solid var(--p-surface-700, #25252f);
+    }
+    .top-post-label {
+      display: block;
+      font-size: 0.65rem;
+      font-weight: 600;
+      text-transform: uppercase;
+      letter-spacing: 0.05em;
+      color: var(--p-text-muted-color, #71717a);
+      margin-bottom: 0.25rem;
+    }
+    .top-post-title {
+      font-size: 0.78rem;
+      font-weight: 500;
+      display: -webkit-box;
+      -webkit-line-clamp: 2;
+      -webkit-box-orient: vertical;
+      overflow: hidden;
+    }
+
+    .unavailable-overlay {
+      display: flex;
+      align-items: center;
+      justify-content: center;
+      min-height: 80px;
+      opacity: 0.7;
+    }
+  `,
+})
+export class PlatformHealthCardsComponent {
+  readonly platforms = input<readonly PlatformSummary[]>([]);
+
+  getColor(platform: string): string {
+    return PLATFORM_COLORS[platform as keyof typeof PLATFORM_COLORS] ?? '#888';
+  }
+
+  getIcon(platform: string): string {
+    return PLATFORM_ICONS[platform as keyof typeof PLATFORM_ICONS] ?? 'pi pi-circle';
+  }
+
+  getLabel(platform: string): string {
+    return PLATFORM_LABELS[platform as keyof typeof PLATFORM_LABELS] ?? platform;
+  }
+
+  getFollowerLabel(platform: string): string {
+    return followerLabel(platform);
+  }
+
+  getEngagementLabel(platform: string): string {
+    return engagementLabel(platform);
+  }
+
+  getUnavailableMessage(platform: string): string {
+    return unavailableMessage(platform);
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.spec.ts
new file mode 100644
index 0000000..51458e2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.spec.ts
@@ -0,0 +1,87 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { SubstackSectionComponent } from './substack-section.component';
+import { SubstackPost } from '../models/dashboard.model';
+
+describe('SubstackSectionComponent', () => {
+  let component: SubstackSectionComponent;
+  let fixture: ComponentFixture<SubstackSectionComponent>;
+
+  const mockPosts: readonly SubstackPost[] = [
+    { title: 'The Future of AI Agents', url: 'https://matthewkruczek.substack.com/p/future-ai-agents', publishedAt: '2026-03-18T10:00:00Z', summary: 'This is a post about AI agents.' },
+    { title: 'Building with Claude Code', url: 'https://matthewkruczek.substack.com/p/claude-code', publishedAt: '2026-03-10T14:00:00Z', summary: 'Tips and tricks for Claude Code workflows.' },
+    { title: 'Enterprise AI Adoption', url: 'https://matthewkruczek.substack.com/p/enterprise-ai', publishedAt: '2026-03-01T09:00:00Z', summary: null },
+  ];
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [SubstackSectionComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(SubstackSectionComponent);
+    component = fixture.componentInstance;
+  });
+
+  it('should render post list from RSS data', () => {
+    fixture.componentRef.setInput('posts', mockPosts);
+    fixture.detectChanges();
+
+    const entries = fixture.nativeElement.querySelectorAll('.substack-post');
+    expect(entries.length).toBe(3);
+    expect(fixture.nativeElement.textContent).toContain('The Future of AI Agents');
+    expect(fixture.nativeElement.textContent).toContain('Building with Claude Code');
+    expect(fixture.nativeElement.textContent).toContain('Enterprise AI Adoption');
+  });
+
+  it('should render post titles as clickable links with target _blank', () => {
+    fixture.componentRef.setInput('posts', mockPosts);
+    fixture.detectChanges();
+
+    const links = fixture.nativeElement.querySelectorAll('.substack-post a');
+    expect(links.length).toBe(3);
+    expect(links[0].getAttribute('href')).toBe('https://matthewkruczek.substack.com/p/future-ai-agents');
+    expect(links[0].getAttribute('target')).toBe('_blank');
+    expect(links[0].getAttribute('rel')).toContain('noopener');
+  });
+
+  it('should format publish dates', () => {
+    fixture.componentRef.setInput('posts', mockPosts);
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    expect(el.textContent).toContain('Mar');
+    expect(el.textContent).toContain('2026');
+  });
+
+  it('should display summary when present', () => {
+    fixture.componentRef.setInput('posts', mockPosts);
+    fixture.detectChanges();
+
+    expect(fixture.nativeElement.textContent).toContain('This is a post about AI agents.');
+  });
+
+  it('should handle null summary gracefully', () => {
+    fixture.componentRef.setInput('posts', mockPosts);
+    fixture.detectChanges();
+
+    const entries = fixture.nativeElement.querySelectorAll('.substack-post');
+    const lastEntry = entries[2] as HTMLElement;
+    expect(lastEntry.querySelector('.post-summary')).toBeNull();
+    expect(lastEntry.textContent).not.toContain('null');
+  });
+
+  it('should show empty state when no posts', () => {
+    fixture.componentRef.setInput('posts', []);
+    fixture.detectChanges();
+
+    expect(fixture.nativeElement.textContent).toContain('No Substack posts found');
+  });
+
+  it('should include Substack branding', () => {
+    fixture.componentRef.setInput('posts', mockPosts);
+    fixture.detectChanges();
+
+    const header = fixture.nativeElement.querySelector('.section-header') as HTMLElement;
+    expect(header.textContent).toContain('Substack');
+    expect(header.querySelector('.pi-at')).toBeTruthy();
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.ts
new file mode 100644
index 0000000..40af828
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.ts
@@ -0,0 +1,127 @@
+import { ChangeDetectionStrategy, Component, input } from '@angular/core';
+import { CommonModule, DatePipe } from '@angular/common';
+import { Card } from 'primeng/card';
+import { SubstackPost } from '../models/dashboard.model';
+
+@Component({
+  selector: 'app-substack-section',
+  standalone: true,
+  imports: [CommonModule, Card, DatePipe],
+  changeDetection: ChangeDetectionStrategy.OnPush,
+  template: `
+    <p-card styleClass="substack-card">
+      <div class="section-header">
+        <i class="pi pi-at section-icon"></i>
+        <span>Substack</span>
+        <a href="https://matthewkruczek.substack.com" target="_blank" rel="noopener noreferrer" class="external-link">
+          <i class="pi pi-external-link"></i>
+        </a>
+      </div>
+
+      @if (posts().length > 0) {
+        <div class="post-list">
+          @for (post of posts(); track post.url; let last = $last) {
+            <div class="substack-post" [class.last]="last">
+              <a [href]="post.url" target="_blank" rel="noopener noreferrer" class="post-title">
+                {{ post.title }}
+              </a>
+              <span class="post-date">{{ post.publishedAt | date:'mediumDate' }}</span>
+              @if (post.summary) {
+                <p class="post-summary">{{ post.summary }}</p>
+              }
+            </div>
+          }
+        </div>
+      } @else {
+        <div class="empty-state">
+          <i class="pi pi-at empty-icon"></i>
+          <span>No Substack posts found</span>
+        </div>
+      }
+    </p-card>
+  `,
+  styles: `
+    :host {
+      display: block;
+    }
+    :host ::ng-deep .substack-card {
+      border-left: 3px solid #ff6719;
+    }
+    .section-header {
+      display: flex;
+      align-items: center;
+      gap: 0.5rem;
+      font-size: 1rem;
+      font-weight: 700;
+      margin-bottom: 1rem;
+    }
+    .section-icon {
+      color: #ff6719;
+      font-size: 1.1rem;
+    }
+    .external-link {
+      margin-left: auto;
+      color: var(--p-text-muted-color, #71717a);
+      font-size: 0.8rem;
+      transition: color 0.2s;
+    }
+    .external-link:hover {
+      color: #ff6719;
+    }
+
+    .post-list {
+      display: flex;
+      flex-direction: column;
+    }
+    .substack-post {
+      padding: 0.6rem 0;
+      border-bottom: 1px solid var(--p-surface-700, #25252f);
+    }
+    .substack-post.last {
+      border-bottom: none;
+    }
+    .post-title {
+      display: block;
+      font-size: 0.9rem;
+      font-weight: 600;
+      color: var(--p-text-color, #e4e4e7);
+      text-decoration: none;
+      transition: text-decoration 0.2s;
+    }
+    .post-title:hover {
+      text-decoration: underline;
+    }
+    .post-date {
+      display: block;
+      font-size: 0.75rem;
+      color: var(--p-text-muted-color, #71717a);
+      margin-top: 0.15rem;
+    }
+    .post-summary {
+      font-size: 0.8rem;
+      color: var(--p-text-muted-color, #71717a);
+      margin: 0.25rem 0 0;
+      display: -webkit-box;
+      -webkit-line-clamp: 2;
+      -webkit-box-orient: vertical;
+      overflow: hidden;
+    }
+
+    .empty-state {
+      display: flex;
+      flex-direction: column;
+      align-items: center;
+      gap: 0.5rem;
+      padding: 1.5rem 0;
+      color: var(--p-text-muted-color, #71717a);
+      font-size: 0.85rem;
+    }
+    .empty-icon {
+      font-size: 1.5rem;
+      opacity: 0.5;
+    }
+  `,
+})
+export class SubstackSectionComponent {
+  readonly posts = input<readonly SubstackPost[]>([]);
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.spec.ts
new file mode 100644
index 0000000..c042331
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.spec.ts
@@ -0,0 +1,112 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { WebsiteAnalyticsSectionComponent } from './website-analytics-section.component';
+import { WebsiteAnalyticsResponse } from '../models/dashboard.model';
+
+describe('WebsiteAnalyticsSectionComponent', () => {
+  let component: WebsiteAnalyticsSectionComponent;
+  let fixture: ComponentFixture<WebsiteAnalyticsSectionComponent>;
+
+  const mockData: WebsiteAnalyticsResponse = {
+    overview: {
+      activeUsers: 1200,
+      sessions: 3400,
+      pageViews: 8900,
+      avgSessionDuration: 142.5,
+      bounceRate: 45.2,
+      newUsers: 800,
+    },
+    topPages: [
+      { pagePath: '/blog/ai-agents', views: 1200, users: 890 },
+      { pagePath: '/about', views: 650, users: 520 },
+      { pagePath: '/projects', views: 430, users: 350 },
+      { pagePath: '/blog/claude-code', views: 380, users: 290 },
+      { pagePath: '/contact', views: 210, users: 180 },
+    ],
+    trafficSources: [
+      { channel: 'Organic Search', sessions: 1800, users: 1400 },
+      { channel: 'Direct', sessions: 900, users: 700 },
+      { channel: 'Social', sessions: 500, users: 400 },
+      { channel: 'Referral', sessions: 200, users: 150 },
+    ],
+    searchQueries: [
+      { query: 'matthew kruczek ai', clicks: 120, impressions: 1500, ctr: 0.08, position: 2.3 },
+      { query: 'enterprise ai agents', clicks: 85, impressions: 3200, ctr: 0.027, position: 8.1 },
+      { query: 'claude code tips', clicks: 45, impressions: 800, ctr: 0.056, position: 4.5 },
+    ],
+  };
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [WebsiteAnalyticsSectionComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(WebsiteAnalyticsSectionComponent);
+    component = fixture.componentInstance;
+  });
+
+  it('should render overview metric cards', () => {
+    fixture.componentRef.setInput('data', mockData);
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    expect(el.textContent).toContain('1,200');
+    expect(el.textContent).toContain('3,400');
+    expect(el.textContent).toContain('8,900');
+    expect(el.textContent).toContain('2m 23s');
+    expect(el.textContent).toContain('45.2%');
+    expect(el.textContent).toContain('800');
+  });
+
+  it('should render top pages table', () => {
+    fixture.componentRef.setInput('data', mockData);
+    fixture.detectChanges();
+
+    const rows = fixture.nativeElement.querySelectorAll('.top-pages-table tbody tr, .top-pages-table .p-datatable-row-group');
+    const el = fixture.nativeElement as HTMLElement;
+    expect(el.textContent).toContain('/blog/ai-agents');
+    expect(el.textContent).toContain('/about');
+  });
+
+  it('should render traffic sources table', () => {
+    fixture.componentRef.setInput('data', mockData);
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    expect(el.textContent).toContain('Organic Search');
+    expect(el.textContent).toContain('Direct');
+    expect(el.textContent).toContain('Social');
+    expect(el.textContent).toContain('Referral');
+  });
+
+  it('should render search queries table with CTR as percentage', () => {
+    fixture.componentRef.setInput('data', mockData);
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    expect(el.textContent).toContain('matthew kruczek ai');
+    expect(el.textContent).toContain('8.0%');
+    expect(el.textContent).toContain('2.3');
+  });
+
+  it('should show skeleton placeholders when data is null', () => {
+    fixture.componentRef.setInput('data', null);
+    fixture.detectChanges();
+
+    const skeletons = fixture.nativeElement.querySelectorAll('p-skeleton');
+    expect(skeletons.length).toBeGreaterThan(0);
+  });
+
+  it('should handle empty arrays gracefully', () => {
+    const emptyData: WebsiteAnalyticsResponse = {
+      overview: mockData.overview,
+      topPages: [],
+      trafficSources: [],
+      searchQueries: [],
+    };
+    fixture.componentRef.setInput('data', emptyData);
+    fixture.detectChanges();
+
+    const el = fixture.nativeElement as HTMLElement;
+    expect(el.textContent).toContain('No data');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.ts
new file mode 100644
index 0000000..8dce296
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.ts
@@ -0,0 +1,233 @@
+import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
+import { CommonModule } from '@angular/common';
+import { TableModule } from 'primeng/table';
+import { Skeleton } from 'primeng/skeleton';
+import { WebsiteAnalyticsResponse } from '../models/dashboard.model';
+
+function formatDuration(seconds: number): string {
+  const mins = Math.floor(seconds / 60);
+  const secs = Math.round(seconds % 60);
+  return `${mins}m ${secs}s`;
+}
+
+@Component({
+  selector: 'app-website-analytics-section',
+  standalone: true,
+  imports: [CommonModule, TableModule, Skeleton],
+  changeDetection: ChangeDetectionStrategy.OnPush,
+  template: `
+    <div class="website-section">
+      <div class="section-header">
+        <i class="pi pi-globe section-icon"></i>
+        <span>Website Analytics</span>
+      </div>
+
+      @if (data(); as d) {
+        <div class="overview-grid">
+          @for (m of overviewMetrics(); track m.label) {
+            <div class="metric-card">
+              <div class="metric-label">{{ m.label }}</div>
+              <div class="metric-value">{{ m.value }}</div>
+            </div>
+          }
+        </div>
+
+        @if (mutableTopPages().length > 0) {
+          <div class="table-section">
+            <h4 class="table-title">Top Pages</h4>
+            <p-table [value]="mutableTopPages()" [rows]="10" class="top-pages-table" styleClass="p-datatable-sm">
+              <ng-template #header>
+                <tr>
+                  <th>Page Path</th>
+                  <th>Views</th>
+                  <th>Users</th>
+                </tr>
+              </ng-template>
+              <ng-template #body let-page>
+                <tr>
+                  <td class="page-path">{{ page.pagePath }}</td>
+                  <td>{{ page.views | number }}</td>
+                  <td>{{ page.users | number }}</td>
+                </tr>
+              </ng-template>
+            </p-table>
+          </div>
+        } @else {
+          <p class="empty-text">No data for this period</p>
+        }
+
+        <div class="two-col-grid">
+          <div>
+            <h4 class="table-title">Traffic Sources</h4>
+            @if (mutableTrafficSources().length > 0) {
+              <p-table [value]="mutableTrafficSources()" styleClass="p-datatable-sm">
+                <ng-template #header>
+                  <tr>
+                    <th>Channel</th>
+                    <th>Sessions</th>
+                    <th>Users</th>
+                  </tr>
+                </ng-template>
+                <ng-template #body let-source>
+                  <tr>
+                    <td>{{ source.channel }}</td>
+                    <td>{{ source.sessions | number }}</td>
+                    <td>{{ source.users | number }}</td>
+                  </tr>
+                </ng-template>
+              </p-table>
+            } @else {
+              <p class="empty-text">No data for this period</p>
+            }
+          </div>
+          <div>
+            <h4 class="table-title">Search Queries</h4>
+            @if (mutableSearchQueries().length > 0) {
+              <p-table [value]="mutableSearchQueries()" styleClass="p-datatable-sm">
+                <ng-template #header>
+                  <tr>
+                    <th>Query</th>
+                    <th>Clicks</th>
+                    <th>Impressions</th>
+                    <th>CTR</th>
+                    <th>Position</th>
+                  </tr>
+                </ng-template>
+                <ng-template #body let-q>
+                  <tr>
+                    <td>{{ q.query }}</td>
+                    <td>{{ q.clicks | number }}</td>
+                    <td>{{ q.impressions | number }}</td>
+                    <td>{{ (q.ctr * 100).toFixed(1) }}%</td>
+                    <td>{{ q.position.toFixed(1) }}</td>
+                  </tr>
+                </ng-template>
+              </p-table>
+            } @else {
+              <p class="empty-text">No data for this period</p>
+            }
+          </div>
+        </div>
+      } @else {
+        <div class="overview-grid">
+          @for (i of skeletonCards; track i) {
+            <p-skeleton width="100%" height="70px" borderRadius="10px" />
+          }
+        </div>
+        <p-skeleton width="100%" height="200px" borderRadius="10px" styleClass="mt-2" />
+      }
+    </div>
+  `,
+  styles: `
+    .website-section {
+      background: var(--p-surface-900, #111118);
+      border: 1px solid var(--p-surface-700, #25252f);
+      border-radius: 12px;
+      padding: 1.25rem;
+    }
+    .section-header {
+      display: flex;
+      align-items: center;
+      gap: 0.5rem;
+      font-size: 1rem;
+      font-weight: 700;
+      margin-bottom: 1rem;
+    }
+    .section-icon {
+      color: #8b5cf6;
+      font-size: 1.1rem;
+    }
+    .overview-grid {
+      display: grid;
+      grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
+      gap: 0.75rem;
+      margin-bottom: 1.25rem;
+    }
+    .metric-card {
+      background: var(--p-surface-800, #1a1a24);
+      border-radius: 10px;
+      padding: 0.75rem 0.9rem;
+    }
+    .metric-label {
+      font-size: 0.68rem;
+      font-weight: 600;
+      text-transform: uppercase;
+      letter-spacing: 0.05em;
+      color: var(--p-text-muted-color, #71717a);
+      margin-bottom: 0.3rem;
+    }
+    .metric-value {
+      font-size: 1.25rem;
+      font-weight: 800;
+      letter-spacing: -0.02em;
+    }
+
+    .table-section {
+      margin-bottom: 1rem;
+    }
+    .table-title {
+      font-size: 0.8rem;
+      font-weight: 700;
+      text-transform: uppercase;
+      letter-spacing: 0.05em;
+      color: var(--p-text-muted-color, #71717a);
+      margin: 0 0 0.5rem 0;
+    }
+    .page-path {
+      font-family: monospace;
+      font-size: 0.82rem;
+      max-width: 300px;
+      overflow: hidden;
+      text-overflow: ellipsis;
+      white-space: nowrap;
+    }
+
+    .two-col-grid {
+      display: grid;
+      grid-template-columns: 1fr 1fr;
+      gap: 1rem;
+    }
+    @media (max-width: 768px) {
+      .two-col-grid { grid-template-columns: 1fr; }
+    }
+
+    .empty-text {
+      font-size: 0.8rem;
+      color: var(--p-text-muted-color, #71717a);
+      font-style: italic;
+    }
+  `,
+})
+export class WebsiteAnalyticsSectionComponent {
+  readonly data = input<WebsiteAnalyticsResponse | null>(null);
+  readonly skeletonCards = [1, 2, 3, 4, 5, 6];
+
+  readonly overviewMetrics = computed(() => {
+    const d = this.data();
+    if (!d) return [];
+    const o = d.overview;
+    return [
+      { label: 'Active Users', value: o.activeUsers.toLocaleString('en-US') },
+      { label: 'Sessions', value: o.sessions.toLocaleString('en-US') },
+      { label: 'Page Views', value: o.pageViews.toLocaleString('en-US') },
+      { label: 'Avg Duration', value: formatDuration(o.avgSessionDuration) },
+      { label: 'Bounce Rate', value: o.bounceRate.toFixed(1) + '%' },
+      { label: 'New Users', value: o.newUsers.toLocaleString('en-US') },
+    ];
+  });
+
+  readonly mutableTopPages = computed(() => {
+    const d = this.data();
+    return d ? [...d.topPages] : [];
+  });
+
+  readonly mutableTrafficSources = computed(() => {
+    const d = this.data();
+    return d ? [...d.trafficSources] : [];
+  });
+
+  readonly mutableSearchQueries = computed(() => {
+    const d = this.data();
+    return d ? [...d.searchQueries] : [];
+  });
+}
