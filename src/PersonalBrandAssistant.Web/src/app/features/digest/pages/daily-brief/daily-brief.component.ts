import { Component, OnInit, inject, signal } from '@angular/core';
import { DigestService } from '../../services/digest.service';
import { Digest, DigestSummary } from '../../models/digest.model';
import { BriefHistoryComponent } from '../../components/brief-history/brief-history.component';
import { BriefDetailComponent } from '../../components/brief-detail/brief-detail.component';

@Component({
  selector: 'app-daily-brief',
  standalone: true,
  imports: [BriefHistoryComponent, BriefDetailComponent],
  template: `
    <div class="brief-layout">
      <aside class="history-pane">
        <app-brief-history [digests]="history()" [selectedId]="selectedId()" (select)="onSelect($event)" />
      </aside>
      <main class="detail-pane">
        @if (loading()) {
          <div class="loading">Loading brief…</div>
        } @else {
          <section class="brief-col">
            <header class="col-head"><span class="col-tag main">AI Brief</span></header>
            <app-brief-detail [digest]="current()" />
          </section>
          <section class="brief-col ms-col">
            <header class="col-head"><span class="col-tag ms">Microsoft</span></header>
            <app-brief-detail [digest]="microsoft()" emptyMessage="No Microsoft brief for this day." />
          </section>
        }
      </main>
    </div>
  `,
  styles: [`
    .brief-layout { display: grid; grid-template-columns: 260px 1fr; height: 100%; min-height: 0; }
    .history-pane { border-right: 1px solid var(--surface-border); overflow-y: auto; background: var(--surface-sidebar); }
    .detail-pane { overflow-y: auto; display: grid; grid-template-columns: 1fr 1fr; }
    .brief-col { min-width: 0; }
    .ms-col { border-left: 1px solid var(--surface-border); }
    .col-head { padding: 16px 28px 0; }
    .col-tag { display: inline-block; font-size: 11px; font-weight: 600; letter-spacing: 0.05em; text-transform: uppercase;
      padding: 3px 10px; border-radius: var(--r-pill); }
    .col-tag.main { background: var(--accent-soft); color: var(--brand-primary); }
    .col-tag.ms { background: color-mix(in srgb, #60a5fa 16%, transparent); color: #60a5fa; }
    .loading { padding: 48px; text-align: center; color: var(--text-secondary); grid-column: 1 / -1; }
    @media (max-width: 1024px) {
      .detail-pane { grid-template-columns: 1fr; }
      .ms-col { border-left: none; border-top: 1px solid var(--surface-border); }
    }
  `],
})
export class DailyBriefComponent implements OnInit {
  private readonly service = inject(DigestService);
  readonly history = signal<DigestSummary[]>([]);
  readonly current = signal<Digest | null>(null);
  readonly microsoft = signal<Digest | null>(null);
  readonly selectedId = signal<string | null>(null);
  readonly loading = signal(false);

  ngOnInit(): void {
    this.loading.set(true);
    this.service.list().subscribe({ next: (h) => this.history.set(h), error: () => this.history.set([]) });
    this.service.getLatest('main').subscribe({
      next: (d) => {
        this.current.set(d);
        this.selectedId.set(d?.id ?? null);
        this.loading.set(false);
        this.loadMicrosoft(d?.date ?? null);
      },
      error: () => { this.current.set(null); this.microsoft.set(null); this.loading.set(false); },
    });
  }

  onSelect(id: string): void {
    if (id === this.selectedId()) return;
    this.selectedId.set(id);
    const date = this.history().find((h) => h.id === id)?.date ?? null;
    this.loading.set(true);
    this.service.getById(id).subscribe({
      next: (d) => { this.current.set(d); this.loading.set(false); },
      error: () => { this.current.set(null); this.loading.set(false); },
    });
    this.loadMicrosoft(date);
  }

  // The Microsoft brief shares the Main brief's date; it may not exist (empty source set) — treat 404 as absent.
  private loadMicrosoft(date: string | null): void {
    if (!date) { this.microsoft.set(null); return; }
    const day = date.slice(0, 10); // yyyy-MM-dd for the by-date route
    this.service.getByDate(day, 'microsoft').subscribe({
      next: (d) => this.microsoft.set(d),
      error: () => this.microsoft.set(null),
    });
  }
}
