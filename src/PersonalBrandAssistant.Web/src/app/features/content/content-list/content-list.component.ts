import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ContentStore } from '../stores/content.store';
import { ContentType } from '../models/content.model';
import { PipelineBarComponent } from './pipeline-bar/pipeline-bar.component';
import { ContentBoardComponent } from './content-board/content-board.component';
import { ContentGridComponent } from './content-grid/content-grid.component';
import { ContentListTableComponent } from './content-list-table/content-list-table.component';
import { DetailDrawerComponent } from './detail-drawer/detail-drawer.component';
import { FiltersPopoverComponent } from './filters-popover/filters-popover.component';
import {
  StudioEmptyStateComponent,
  IdeaSuggestion,
} from './studio-empty-state/studio-empty-state.component';

const IDEA_SUGGESTIONS: IdeaSuggestion[] = [
  {
    title: 'A lesson from this week',
    blurb: 'Turn a small win or failure into a short, honest post.',
    topic: 'weekly lesson',
    type: ContentType.LinkedInPost,
  },
  {
    title: 'A take on where AI is heading',
    blurb: 'Stake a clear position and defend it.',
    topic: 'AI direction',
    type: ContentType.Tweet,
  },
  {
    title: 'A deep-dive you keep meaning to write',
    blurb: 'The long-form piece living in your head.',
    topic: 'deep dive',
    type: ContentType.BlogPost,
  },
  {
    title: 'Behind the build',
    blurb: 'Show how something you shipped actually works.',
    topic: 'behind the build',
    type: ContentType.SubstackNewsletter,
  },
];

@Component({
  selector: 'app-content-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    ButtonModule,
    InputTextModule,
    PipelineBarComponent,
    ContentBoardComponent,
    ContentGridComponent,
    ContentListTableComponent,
    DetailDrawerComponent,
    FiltersPopoverComponent,
    StudioEmptyStateComponent,
  ],
  template: `
    <div class="studio" data-testid="content-list-page">
      <header class="studio-header">
        <div class="title-row">
          <div class="titles">
            <h1>Content Studio</h1>
            <p class="subtitle">{{ subtitle() }}</p>
          </div>
          <p-button
            label="New Content"
            icon="pi pi-plus"
            (onClick)="onNewContent()"
            data-testid="new-content-btn" />
        </div>

        <div class="controls">
          <div class="search-wrapper">
            <i class="pi pi-search"></i>
            <input
              type="text"
              pInputText
              placeholder="Search title or tags…"
              [(ngModel)]="searchText"
              (input)="onSearchInput()"
              data-testid="search-input" />
          </div>

          <div class="view-toggle" data-testid="view-toggle">
            <button
              type="button"
              [class.on]="store.viewMode() === 'board'"
              (click)="store.setView('board')"
              data-testid="toggle-board">
              <i class="pi pi-th-large"></i> Board
            </button>
            <button
              type="button"
              [class.on]="store.viewMode() === 'table'"
              (click)="store.setView('table')"
              data-testid="toggle-table">
              <i class="pi pi-list"></i> Table
            </button>
          </div>

          <p-button
            label="Filters"
            icon="pi pi-filter"
            severity="secondary"
            [outlined]="true"
            (onClick)="filters.toggle($event)"
            data-testid="filters-btn" />
          <app-filters-popover #filters />
        </div>
      </header>

      <app-pipeline-bar />

      <div class="views">
        @if (store.allContents().length === 0 && !store.loading()) {
          <app-studio-empty-state variant="inspire" [suggestions]="ideaSuggestions" />
        } @else if (store.filtered().length === 0 && !store.loading()) {
          <app-studio-empty-state variant="filtered" (clearFilters)="onClearFilters()" />
        } @else {
          @switch (store.viewMode()) {
            @case ('board') {
              <app-content-board (openCard)="onOpen($event)" />
            }
            @case ('grid') {
              <app-content-grid [contents]="store.filtered()" (openCard)="onOpen($event)" />
            }
            @case ('table') {
              <app-content-list-table [contents]="store.filtered()" (openRow)="onOpen($event)" />
            }
          }
        }
      </div>

      <app-detail-drawer [contentId]="selectedId()" (closed)="onCloseDrawer()" />
    </div>
  `,
  styles: [`
    .studio {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
      overflow: hidden;
    }
    .studio-header {
      display: flex;
      flex-direction: column;
      gap: 14px;
      padding: 22px 28px 0;
    }
    .title-row {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
    }
    .titles h1 {
      font-family: var(--font-display);
      font-size: 30px;
      font-weight: 400;
      color: var(--text-primary);
      margin: 0;
    }
    .subtitle {
      font-size: 14px;
      color: var(--text-secondary);
      margin: 4px 0 0;
    }
    .controls {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }
    .search-wrapper {
      position: relative;
      flex: 1;
      min-width: 220px;
      max-width: 420px;
    }
    .search-wrapper i {
      position: absolute;
      left: 12px;
      top: 50%;
      transform: translateY(-50%);
      color: var(--text-muted);
      font-size: 14px;
    }
    .search-wrapper input {
      width: 100%;
      padding-left: 34px;
    }
    .view-toggle {
      display: inline-flex;
      gap: 2px;
      padding: 3px;
      background: var(--surface-card);
      border: 1px solid var(--surface-border);
      border-radius: var(--r-pill);
    }
    .view-toggle button {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 6px 14px;
      border: none;
      background: transparent;
      color: var(--text-secondary);
      border-radius: var(--r-pill);
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: background 0.14s, color 0.14s;
    }
    .view-toggle button.on {
      background: var(--surface-elevated);
      color: var(--text-primary);
    }
    .views {
      flex: 1;
      min-height: 0;
      overflow: auto;
    }
  `],
})
export class ContentListComponent implements OnInit {
  readonly store = inject(ContentStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly ideaSuggestions = IDEA_SUGGESTIONS;

  searchText = '';
  private readonly search$ = new Subject<string>();
  readonly selectedId = signal<string | null>(null);

  readonly subtitle = computed(() => {
    const n = this.store.allContents().length;
    return `${n} ${n === 1 ? 'piece' : 'pieces'} moving through your pipeline`;
  });

  constructor() {
    this.search$
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe((term) => this.store.setSearch(term));
  }

  ngOnInit(): void {
    this.store.loadAll();
  }

  onSearchInput(): void {
    this.search$.next(this.searchText);
  }

  onNewContent(): void {
    this.router.navigate(['/content/new']);
  }

  onOpen(id: string): void {
    this.selectedId.set(id);
  }

  onCloseDrawer(): void {
    this.selectedId.set(null);
  }

  onClearFilters(): void {
    this.store.setActiveStatus(null);
    this.store.setSearch('');
    this.searchText = '';
    this.store.setFilter('platform', undefined);
    this.store.setFilter('contentType', undefined);
    this.store.setFilter('dateFrom', undefined);
    this.store.setFilter('dateTo', undefined);
  }
}
