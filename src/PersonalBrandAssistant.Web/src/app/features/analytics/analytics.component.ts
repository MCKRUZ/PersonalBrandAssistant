import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TabsModule } from 'primeng/tabs';
import { OverviewComponent } from './overview/overview.component';
import { WebsiteAnalyticsComponent } from './website/website-analytics.component';
import { ChannelAnalyticsComponent } from './channel/channel-analytics.component';

type TabKey = 'overview' | 'website' | 'youtube' | 'instagram' | 'tiktok';

const TAB_KEYS: readonly TabKey[] = ['overview', 'website', 'youtube', 'instagram', 'tiktok'];

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule, TabsModule, OverviewComponent, WebsiteAnalyticsComponent, ChannelAnalyticsComponent],
  template: `
    <p-tabs [value]="activeTab()" (valueChange)="onTabChange($event)" class="analytics-tabs">
      <p-tablist>
        <p-tab value="overview">Overview</p-tab>
        <p-tab value="website">Website</p-tab>
        <p-tab value="youtube">YouTube</p-tab>
        <p-tab value="instagram">Instagram</p-tab>
        <p-tab value="tiktok">TikTok</p-tab>
      </p-tablist>
      <p-tabpanels>
        <p-tabpanel value="overview">
          @if (visited().has('overview')) { <app-overview /> }
        </p-tabpanel>
        <p-tabpanel value="website">
          @if (visited().has('website')) { <app-website-analytics /> }
        </p-tabpanel>
        <p-tabpanel value="youtube">
          @if (visited().has('youtube')) { <app-channel-analytics [platform]="'youtube'" /> }
        </p-tabpanel>
        <p-tabpanel value="instagram">
          @if (visited().has('instagram')) { <app-channel-analytics [platform]="'instagram'" /> }
        </p-tabpanel>
        <p-tabpanel value="tiktok">
          @if (visited().has('tiktok')) { <app-channel-analytics [platform]="'tiktok'" /> }
        </p-tabpanel>
      </p-tabpanels>
    </p-tabs>
  `,
  styles: [`
    :host { display: block; }
    .analytics-tabs { display: block; }
  `],
})
export class AnalyticsComponent {
  readonly activeTab = signal<TabKey>('overview');
  // A tab's child mounts (and fires its HTTP) only after its tab is first opened.
  readonly visited = signal<ReadonlySet<TabKey>>(new Set<TabKey>(['overview']));

  onTabChange(value: string | number | undefined): void {
    if (!TAB_KEYS.includes(value as TabKey)) return;
    const tab = value as TabKey;
    this.activeTab.set(tab);
    if (!this.visited().has(tab)) {
      this.visited.update(prev => new Set<TabKey>([...prev, tab]));
    }
  }
}
