import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DigestService } from '../../services/digest.service';
import { Digest } from '../../models/digest.model';

@Component({
  selector: 'app-daily-brief',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (digest(); as d) {
      <article class="daily-brief">
        <header>
          <h1>{{ d.title }}</h1>
          <p class="intro">{{ d.intro }}</p>
        </header>
        <ol class="brief-items">
          @for (item of d.items; track item.ideaId) {
            <li>
              <span class="rank">{{ item.rank }}</span>
              <span class="score">{{ item.score }}/10</span>
              @if (item.url) {
                <a [href]="item.url" target="_blank" rel="noopener">{{ item.title }}</a>
              } @else {
                <span>{{ item.title }}</span>
              }
              <p class="why">{{ item.whyItMatters }}</p>
            </li>
          }
        </ol>
      </article>
    } @else {
      <p class="empty">No daily brief yet. Check back after the next run.</p>
    }
  `,
})
export class DailyBriefComponent implements OnInit {
  private readonly service = inject(DigestService);
  readonly digest = signal<Digest | null>(null);

  ngOnInit(): void {
    this.service.getLatest().subscribe({
      next: (d) => this.digest.set(d),
      error: () => this.digest.set(null),
    });
  }
}
