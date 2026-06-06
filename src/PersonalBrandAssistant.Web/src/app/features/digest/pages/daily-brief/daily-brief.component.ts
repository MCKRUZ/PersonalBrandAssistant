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
          <app-brief-detail [digest]="current()" />
        }
      </main>
    </div>
  `,
  styles: [`
    .brief-layout { display: grid; grid-template-columns: 260px 1fr; height: 100%; min-height: 0; }
    .history-pane { border-right: 1px solid var(--surface-border); overflow-y: auto; background: var(--surface-sidebar); }
    .detail-pane { overflow-y: auto; }
    .loading { padding: 48px; text-align: center; color: var(--text-secondary); }
  `],
})
export class DailyBriefComponent implements OnInit {
  private readonly service = inject(DigestService);
  readonly history = signal<DigestSummary[]>([]);
  readonly current = signal<Digest | null>(null);
  readonly selectedId = signal<string | null>(null);
  readonly loading = signal(false);

  ngOnInit(): void {
    this.service.list().subscribe({ next: (h) => this.history.set(h), error: () => this.history.set([]) });
    this.service.getLatest().subscribe({
      next: (d) => { this.current.set(d); this.selectedId.set(d?.id ?? null); },
      error: () => this.current.set(null),
    });
  }

  onSelect(id: string): void {
    if (id === this.selectedId()) return;
    this.selectedId.set(id);
    this.loading.set(true);
    this.service.getById(id).subscribe({
      next: (d) => { this.current.set(d); this.loading.set(false); },
      error: () => { this.current.set(null); this.loading.set(false); },
    });
  }
}
