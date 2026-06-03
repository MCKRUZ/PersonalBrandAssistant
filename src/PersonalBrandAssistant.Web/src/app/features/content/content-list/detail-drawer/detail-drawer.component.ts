import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { DrawerModule } from 'primeng/drawer';
import { ButtonModule } from 'primeng/button';
import { ContentStore } from '../../stores/content.store';
import { ContentService } from '../../services/content.service';
import { Content, ContentStatus } from '../../models/content.model';
import {
  STATUS_META,
  TYPE_GLYPH,
  formatContentType,
  nextStatus,
  relativeTime,
} from '../content-display.utils';
import { StatusTagComponent } from '../../shared/status-tag.component';
import { VoiceScoreRingComponent } from '../../shared/voice-score-ring.component';
import { PlatformDotComponent } from '../../shared/platform-dot.component';
import { ScheduleDialogComponent } from '../schedule-dialog/schedule-dialog.component';

const BODY_PREVIEW_CHARS = 360;

/**
 * Right-hand detail drawer. Header status tag + serif title + meta list + body preview. Footer
 * "Open in editor" routes to /content/:id; the context action either routes to the editor for
 * publishing (Approved/Scheduled) or moves the piece to its next status (opening the schedule
 * dialog when the next status is Scheduled).
 */
@Component({
  selector: 'app-detail-drawer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DrawerModule,
    ButtonModule,
    StatusTagComponent,
    VoiceScoreRingComponent,
    PlatformDotComponent,
    ScheduleDialogComponent,
  ],
  template: `
    <p-drawer
      [visible]="open()"
      (visibleChange)="onVisibleChange($event)"
      position="right"
      [modal]="true"
      styleClass="detail-drawer"
      [showCloseIcon]="false">
      @if (content(); as c) {
        <div class="dd" data-testid="detail-drawer">
          <div class="dd-head">
            <app-status-tag [status]="c.status" />
            <button class="close" type="button" (click)="closed.emit()" data-testid="drawer-close"
              aria-label="Close">✕</button>
          </div>

          <div class="dd-type">
            <span class="glyph">{{ glyph(c) }}</span>
            {{ formatType(c.contentType) }}
          </div>

          <h2 class="dd-title">{{ c.title }}</h2>

          <div class="dd-meta">
            <div class="dm-row">
              <span class="dm-k">Voice</span>
              <span class="dm-v"><app-voice-score-ring [score]="c.voiceScore" [size]="30" /></span>
            </div>
            <div class="dm-row">
              <span class="dm-k">Platforms</span>
              <span class="dm-v platforms">
                @for (p of c.targetPlatforms; track p) {
                  <app-platform-dot [platform]="p" variant="tile" />
                }
              </span>
            </div>
            <div class="dm-row">
              <span class="dm-k">{{ c.scheduledAt ? 'Scheduled' : 'Updated' }}</span>
              <span class="dm-v mono">{{ relative(c.scheduledAt ?? c.updatedAt) }}</span>
            </div>
            @if (c.tags.length > 0) {
              <div class="dm-row">
                <span class="dm-k">Tags</span>
                <span class="dm-v mono">{{ tagLine(c) }}</span>
              </div>
            }
          </div>

          @if (bodyPreview()) {
            <p class="dd-body" data-testid="drawer-body">{{ bodyPreview() }}</p>
          }

          <div class="dd-foot">
            <p-button
              label="Open in editor"
              severity="secondary"
              [text]="true"
              (onClick)="openEditor(c.id)"
              data-testid="open-editor-btn" />
            @if (isPublishable(c.status)) {
              <p-button
                label="Publish →"
                (onClick)="openEditor(c.id)"
                data-testid="drawer-context-btn" />
            } @else {
              @if (contextNext(); as n) {
                <p-button
                  [label]="'Move to ' + statusLabel(n) + ' →'"
                  (onClick)="onContextAction(c, n)"
                  data-testid="drawer-context-btn" />
              }
            }
          </div>
        </div>
      }
    </p-drawer>

    <app-schedule-dialog
      [visible]="scheduleVisible()"
      (confirmed)="onScheduleConfirm($event)"
      (cancelled)="scheduleVisible.set(false)" />
  `,
  styles: [`
    .dd {
      display: flex;
      flex-direction: column;
      height: 100%;
    }
    .dd-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 14px;
    }
    .close {
      width: 30px;
      height: 30px;
      border-radius: 7px;
      border: 1px solid var(--surface-border);
      background: var(--surface-card);
      color: var(--text-secondary);
      cursor: pointer;
      font-size: 13px;
      transition: background 0.14s, color 0.14s;
    }
    .close:hover {
      background: var(--surface-hover);
      color: var(--text-primary);
    }
    .dd-type {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 0.5px;
      text-transform: uppercase;
      color: var(--text-secondary);
    }
    .dd-type .glyph {
      font-size: 14px;
    }
    .dd-title {
      font-family: var(--font-display);
      font-size: 23px;
      font-weight: 400;
      line-height: 1.18;
      color: var(--text-primary);
      margin: 10px 0 20px;
    }
    .dm-row {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 12px 0;
      border-bottom: 1px solid var(--surface-border);
    }
    .dm-k {
      width: 96px;
      flex-shrink: 0;
      font-size: 12px;
      letter-spacing: 0.5px;
      text-transform: uppercase;
      color: var(--text-muted);
    }
    .dm-v {
      color: var(--text-primary);
      font-size: 13px;
    }
    .dm-v.platforms {
      display: inline-flex;
      gap: 6px;
      flex-wrap: wrap;
    }
    .dm-v.mono {
      font-family: var(--font-mono);
      font-size: 12px;
      color: var(--text-secondary);
    }
    .dd-body {
      margin: 20px 0 0;
      font-size: 13.5px;
      line-height: 1.6;
      color: var(--text-secondary);
      white-space: pre-wrap;
    }
    .dd-foot {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 10px;
      margin-top: auto;
      padding-top: 22px;
    }
  `],
})
export class DetailDrawerComponent {
  private readonly store = inject(ContentStore);
  private readonly contentService = inject(ContentService);
  private readonly router = inject(Router);

  /** The selected content id (null = closed). */
  readonly contentId = input<string | null>(null);
  readonly closed = output<void>();

  readonly scheduleVisible = signal(false);
  private readonly body = signal<string | null>(null);

  readonly open = computed(() => this.contentId() !== null);

  readonly content = computed<Content | null>(() => {
    const id = this.contentId();
    if (!id) return null;
    return this.store.allContents().find((c) => c.id === id) ?? null;
  });

  /** Next legal status for the context "Move to {next}" action (null when publishable/terminal). */
  readonly contextNext = computed<ContentStatus | null>(() => {
    const c = this.content();
    return c ? nextStatus(c.status) : null;
  });

  readonly bodyPreview = computed(() => {
    const text = this.body();
    if (!text) return '';
    return text.length > BODY_PREVIEW_CHARS ? text.slice(0, BODY_PREVIEW_CHARS) + '…' : text;
  });

  readonly formatType = formatContentType;

  constructor() {
    // Fetch the full body for the preview whenever the selected id changes.
    effect(() => {
      const id = this.contentId();
      this.body.set(null);
      if (!id) return;
      this.contentService.get(id).subscribe({
        next: (detail) => this.body.set(detail.body ?? ''),
        error: () => this.body.set(''),
      });
    });
  }

  glyph(content: Content): string {
    return TYPE_GLYPH[content.contentType];
  }

  statusLabel(status: ContentStatus): string {
    return STATUS_META[status].label;
  }

  relative(iso: string): string {
    return relativeTime(iso);
  }

  tagLine(content: Content): string {
    return content.tags.map((t) => `#${t}`).join(' ');
  }

  next(status: ContentStatus): ContentStatus | null {
    return nextStatus(status);
  }

  isPublishable(status: ContentStatus): boolean {
    return status === ContentStatus.Approved || status === ContentStatus.Scheduled;
  }

  onVisibleChange(visible: boolean): void {
    if (!visible) this.closed.emit();
  }

  openEditor(id: string): void {
    this.router.navigate(['/content', id]);
    this.closed.emit();
  }

  onContextAction(content: Content, target: ContentStatus): void {
    if (target === ContentStatus.Scheduled) {
      this.scheduleVisible.set(true);
      return;
    }
    this.store.transition(content.id, target);
    this.closed.emit();
  }

  onScheduleConfirm(scheduledAt: string): void {
    this.scheduleVisible.set(false);
    const content = this.content();
    if (!content) return;
    this.contentService.schedule(content.id, { scheduledAt }).subscribe({
      next: () => {
        this.store.loadAll();
        this.closed.emit();
      },
    });
  }
}
