import {
  Component,
  HostListener,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { Platform, PublishStatus } from '../../models/content.model';
import type { ContentDetail, PlatformConnectionStatus } from '../../models/content.model';
import { ContentService } from '../../services/content.service';
import { PLATFORM_META, PUBLISHABLE_PLATFORMS, deliveryBadge } from '../../models/platform-metadata';
import { PlatformDotComponent } from '../../shared/platform-dot.component';
import { DeliveryBadgeComponent } from './delivery-badge.component';
import { PublishResultComponent, ResultRow } from './publish-result.component';
import { toBlocks, plainText } from './markdown-blocks';
import { splitThread } from './thread-splitter';
import { BlogPreviewComponent } from './previews/blog-preview.component';
import { MediumPreviewComponent } from './previews/medium-preview.component';
import { SubstackPreviewComponent } from './previews/substack-preview.component';
import { LinkedinPreviewComponent } from './previews/linkedin-preview.component';
import { TwitterPreviewComponent } from './previews/twitter-preview.component';

const POLL_MS = 2000;
const POLL_CAP_MS = 30000;
const OPEN_URLS: Partial<Record<Platform, string>> = {
  [Platform.Medium]: 'https://medium.com/new-story',
};

@Component({
  selector: 'app-publish-modal',
  standalone: true,
  imports: [
    A11yModule,
    PlatformDotComponent,
    DeliveryBadgeComponent,
    PublishResultComponent,
    BlogPreviewComponent,
    MediumPreviewComponent,
    SubstackPreviewComponent,
    LinkedinPreviewComponent,
    TwitterPreviewComponent,
  ],
  templateUrl: './publish-modal.component.html',
  styleUrl: './publish-modal.component.scss',
})
export class PublishModalComponent implements OnDestroy {
  private readonly contentService = inject(ContentService);

  readonly visible = input.required<boolean>();
  readonly content = input.required<ContentDetail>();
  readonly connectedPlatforms = input.required<PlatformConnectionStatus[]>();
  readonly mode = input<'publish' | 'schedule'>('publish');

  readonly confirm = output<{ platforms: Platform[]; scheduledAt?: string }>();
  readonly cancel = output<void>();

  readonly publishable = PUBLISHABLE_PLATFORMS;
  readonly meta = PLATFORM_META;

  private readonly selected = signal<Platform[]>([]);
  readonly activeTab = signal<Platform | null>(null);
  readonly scheduledAt = signal('');
  readonly result = signal<ResultRow[] | null>(null);

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private pollStarted = 0;

  constructor() {
    effect(() => {
      if (this.visible()) {
        this.selected.set([]);
        this.scheduledAt.set('');
        this.result.set(null);
        this.stopPolling();
      } else {
        // Stop any in-flight status polling when the modal is hidden (component is not destroyed).
        this.stopPolling();
      }
    });
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.visible() && !this.result()) this.onCancel();
  }

  // --- derived content ---
  readonly blocks = computed(() => toBlocks(this.content().body ?? ''));
  readonly plain = computed(() => plainText(this.content().body ?? ''));
  /** Derived, presentation-only subtitle = first paragraph (never persisted). */
  readonly subtitle = computed(() => this.blocks().find((b) => b.type === 'p')?.text ?? '');

  // --- destinations ---
  readonly selectedPlatforms = computed<Platform[]>(() => {
    const primary = this.content().primaryPlatform;
    const secondary = this.selected();
    return [primary, ...secondary.filter((p) => p !== primary)];
  });

  readonly summary = computed(() => {
    const sel = this.selectedPlatforms();
    const auto = sel.filter((p) => PLATFORM_META[p].delivery === 'auto').length;
    return { n: sel.length, auto, manual: sel.length - auto };
  });

  isPrimary(p: Platform): boolean {
    return p === this.content().primaryPlatform;
  }
  isSelected(p: Platform): boolean {
    return this.selectedPlatforms().includes(p);
  }
  isConnected(p: Platform): boolean {
    return this.connectedPlatforms().some((c) => c.platform === p && c.isConnected);
  }
  toggle(p: Platform): void {
    if (this.isPrimary(p) || this.mode() === 'schedule') return;
    const cur = this.selected();
    this.selected.set(cur.includes(p) ? cur.filter((x) => x !== p) : [...cur, p]);
  }

  /** Usage string per destination: char count for capped platforms, tweet count for Twitter. */
  usage(p: Platform): string {
    const limit = PLATFORM_META[p].charLimit;
    if (p === Platform.Twitter) return `${splitThread(this.plain(), 280).length} tweets`;
    if (limit) return `${this.plain().length}/${limit}`;
    return '';
  }
  overLimit(p: Platform): boolean {
    const limit = PLATFORM_META[p].charLimit;
    return p !== Platform.Twitter && !!limit && this.plain().length > limit;
  }
  badge(p: Platform) {
    return deliveryBadge(PLATFORM_META[p], this.isConnected(p));
  }

  // --- preview tabs ---
  readonly tabs = computed(() => this.selectedPlatforms());
  setTab(p: Platform): void {
    this.activeTab.set(p);
  }
  currentTab = computed(() => this.activeTab() ?? this.selectedPlatforms()[0] ?? null);

  caption(p: Platform): string {
    const m = PLATFORM_META[p];
    if (m.delivery === 'manual') return `How it appears on ${m.label} · you post this one`;
    return this.isConnected(p)
      ? `How it appears on ${m.label} · deploys automatically`
      : `How it appears on ${m.label} · connect to auto-deploy`;
  }

  asInputValue(e: Event): string {
    return (e.target as HTMLInputElement).value;
  }

  readonly canConfirm = computed(
    () => this.selectedPlatforms().length > 0 && !(this.mode() === 'schedule' && !this.scheduledAt())
  );

  onConfirm(): void {
    const payload: { platforms: Platform[]; scheduledAt?: string } = {
      platforms: this.selectedPlatforms(),
    };
    const scheduled = this.mode() === 'schedule' && this.scheduledAt();
    if (scheduled) payload.scheduledAt = new Date(this.scheduledAt()).toISOString();
    this.confirm.emit(payload);

    this.result.set(this.buildRows(!!scheduled));
    if (!scheduled && this.result()!.some((r) => r.state === 'publishing')) this.startPolling();
  }

  onCancel(): void {
    this.cancel.emit();
  }

  retry(p: Platform): void {
    this.contentService.retryPlatform(this.content().id, p).subscribe();
    this.result.set(
      this.result()!.map((r) => (r.platform === p ? { ...r, state: 'publishing' } : r))
    );
    this.startPolling();
  }

  private buildRows(scheduled: boolean): ResultRow[] {
    return this.selectedPlatforms().map((p) => {
      const m = PLATFORM_META[p];
      if (scheduled) {
        return { platform: p, code: m.code, label: m.label, mode: 'scheduled', state: 'scheduled', scheduledAt: this.scheduledAt() };
      }
      if (m.delivery === 'manual' || !this.isConnected(p)) {
        return {
          platform: p, code: m.code, label: m.label, mode: 'manual', state: 'ready',
          copyText: this.content().body ?? '', openUrl: OPEN_URLS[p],
        };
      }
      return { platform: p, code: m.code, label: m.label, mode: 'auto', state: 'publishing' };
    });
  }

  private startPolling(): void {
    this.stopPolling();
    this.pollStarted = Date.now();
    this.pollTimer = setInterval(() => this.poll(), POLL_MS);
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private poll(): void {
    this.contentService.getPublishStatus(this.content().id).subscribe({
      next: (res) => {
        const byPlatform = new Map(res.platformStatuses.map((s) => [s.platform, s]));
        const rows = this.result()!.map((r) => {
          if (r.mode !== 'auto' || r.state === 'published' || r.state === 'failed') return r;
          const s = byPlatform.get(r.platform);
          if (!s) return r;
          if (s.publishStatus === PublishStatus.Published)
            return { ...r, state: 'published' as const, url: s.publishedUrl ?? undefined };
          if (s.publishStatus === PublishStatus.Failed) return { ...r, state: 'failed' as const };
          return r;
        });
        this.result.set(rows);
        const settled = rows.every((r) => r.mode !== 'auto' || r.state === 'published' || r.state === 'failed');
        if (settled || Date.now() - this.pollStarted > POLL_CAP_MS) this.stopPolling();
      },
      error: () => this.stopPolling(),
    });
  }
}
