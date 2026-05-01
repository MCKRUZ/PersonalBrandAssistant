import { Component, inject, OnInit, signal, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged, skip } from 'rxjs';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { Select } from 'primeng/select';
import { InputTextModule } from 'primeng/inputtext';
import { Tag } from 'primeng/tag';
import { PageHeaderComponent, PageAction } from '../../shared/components/page-header/page-header.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { RelativeTimePipe } from '../../shared/pipes/relative-time.pipe';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Toast } from 'primeng/toast';
import { ContentPipelineDialogComponent } from './components/content-pipeline-dialog.component';
import { ContentStore } from './store/content.store';
import { ContentService } from './services/content.service';
import { Content, ContentStatus, ContentType, PlatformType } from '../../shared/models';
import { PLATFORM_LABELS } from '../../shared/utils/platform-icons';

@Component({
  selector: 'app-content-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TableModule, ButtonModule, Select, InputTextModule, Tag, ConfirmDialog, Toast,
    PageHeaderComponent, EmptyStateComponent, LoadingSpinnerComponent,
    StatusBadgeComponent, RelativeTimePipe,
    ContentPipelineDialogComponent,
  ],
  template: `
    <app-content-pipeline-dialog #pipelineDialog (closed)="store.loadContent(store.filters())" />
    <app-page-header title="Content" [actions]="actions" />

    <div class="filter-bar">
      <p-select
        [(ngModel)]="statusFilter"
        [options]="statusOptions"
        optionLabel="label"
        optionValue="value"
        placeholder="All Statuses"
        [showClear]="true"
        (onChange)="applyFilters()"
        styleClass="w-12rem"
      />
      <p-select
        [(ngModel)]="typeFilter"
        [options]="typeOptions"
        optionLabel="label"
        optionValue="value"
        placeholder="All Types"
        [showClear]="true"
        (onChange)="applyFilters()"
        styleClass="w-12rem"
      />
      <p-select
        [(ngModel)]="platformFilter"
        [options]="platformOptions"
        optionLabel="label"
        optionValue="value"
        placeholder="All Platforms"
        [showClear]="true"
        (onChange)="applyFilters()"
        styleClass="w-12rem"
      />
      <span class="search-wrapper">
        <i class="pi pi-search"></i>
        <input pInputText [ngModel]="searchText()" (ngModelChange)="searchText.set($event)" placeholder="Search content..." class="search-input" />
      </span>
    </div>

    @if (store.loading() && !store.hasContent()) {
      <app-loading-spinner message="Loading content..." />
    } @else if (!store.hasContent()) {
      <app-empty-state message="No content yet. Create your first piece!" icon="pi pi-file" />
    } @else {
      <p-table [value]="$any(store.items())" [rowHover]="true" styleClass="p-datatable-sm">
        <ng-template #header>
          <tr>
            <th>Title</th>
            <th>Type</th>
            <th>Platform</th>
            <th>Status</th>
            <th>Created</th>
            <th style="width: 7rem">Actions</th>
          </tr>
        </ng-template>
        <ng-template #body let-item>
          <tr class="cursor-pointer" (click)="viewContent(item)">
            <td>{{ item.title || 'Untitled' }}</td>
            <td><p-tag [value]="item.contentType" severity="info" /></td>
            <td>{{ getPlatformLabel(item.targetPlatforms?.[0]) }}</td>
            <td><app-status-badge [status]="item.status" /></td>
            <td>{{ item.createdAt | relativeTime }}</td>
            <td>
              <p-button icon="pi pi-pencil" [text]="true" (onClick)="editContent(item); $event.stopPropagation()" />
              <p-button icon="pi pi-trash" [text]="true" severity="danger" (onClick)="deleteContent(item); $event.stopPropagation()" />
            </td>
          </tr>
        </ng-template>
      </p-table>

      @if (store.hasMore()) {
        <div class="flex justify-content-center mt-3">
          <p-button label="Load More" icon="pi pi-arrow-down" [text]="true" (onClick)="store.loadMore(undefined)" [loading]="store.loading()" />
        </div>
      }
    }
    <p-confirmDialog />
    <p-toast />
  `,
  styles: `
    .cursor-pointer { cursor: pointer; }
    .filter-bar {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin-bottom: 1rem;
      flex-wrap: wrap;
    }
    .search-wrapper {
      position: relative;
      display: flex;
      align-items: center;
      flex: 1;
      min-width: 200px;
    }
    .search-wrapper i {
      position: absolute;
      left: 0.75rem;
      color: var(--p-text-muted-color);
      pointer-events: none;
    }
    .search-input {
      width: 100%;
      padding-left: 2.25rem;
    }
  `,
  providers: [ConfirmationService, MessageService],
})
export class ContentListComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly contentService = inject(ContentService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  readonly store = inject(ContentStore);

  @ViewChild('pipelineDialog') pipelineDialog!: ContentPipelineDialogComponent;

  statusFilter?: ContentStatus;
  typeFilter?: ContentType;
  platformFilter?: PlatformType;
  searchText = signal('');

  readonly actions: PageAction[] = [
    { label: 'New Content', icon: 'pi pi-plus', command: () => this.router.navigate(['/content/new']) },
    { label: 'AI Pipeline', icon: 'pi pi-sparkles', command: () => this.pipelineDialog.open() },
  ];

  readonly statusOptions = [
    { label: 'Draft', value: 'Draft' },
    { label: 'Review', value: 'Review' },
    { label: 'Approved', value: 'Approved' },
    { label: 'Scheduled', value: 'Scheduled' },
    { label: 'Published', value: 'Published' },
    { label: 'Failed', value: 'Failed' },
    { label: 'Archived', value: 'Archived' },
  ];

  readonly typeOptions = [
    { label: 'Blog Post', value: 'BlogPost' },
    { label: 'Social Post', value: 'SocialPost' },
    { label: 'Thread', value: 'Thread' },
    { label: 'Video Description', value: 'VideoDescription' },
  ];

  readonly platformOptions = [
    { label: 'Twitter/X', value: 'TwitterX' },
    { label: 'LinkedIn', value: 'LinkedIn' },
    { label: 'Instagram', value: 'Instagram' },
    { label: 'YouTube', value: 'YouTube' },
    { label: 'Reddit', value: 'Reddit' },
    { label: 'Personal Blog', value: 'PersonalBlog' },
    { label: 'Substack', value: 'Substack' },
  ];

  constructor() {
    toObservable(this.searchText).pipe(
      skip(1),
      debounceTime(300),
      distinctUntilChanged(),
      takeUntilDestroyed(),
    ).subscribe(() => this.applyFilters());
  }

  ngOnInit() {
    this.store.loadContent({});
  }

  applyFilters() {
    this.store.loadContent({
      status: this.statusFilter,
      contentType: this.typeFilter,
      platform: this.platformFilter,
      search: this.searchText() || undefined,
    });
  }

  getPlatformLabel(platform?: string): string {
    if (!platform) return '—';
    return (PLATFORM_LABELS as Record<string, string>)[platform] ?? platform;
  }

  viewContent(item: Content) {
    this.router.navigate(['/content', item.id]);
  }

  editContent(item: Content) {
    this.router.navigate(['/content', item.id, 'edit']);
  }

  deleteContent(item: Content) {
    this.confirmationService.confirm({
      message: `Delete "${item.title || 'Untitled'}"? This cannot be undone.`,
      header: 'Delete Content',
      icon: 'pi pi-trash',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => {
        this.contentService.remove(item.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Deleted' });
            this.applyFilters();
          },
          error: () => {
            this.messageService.add({ severity: 'error', summary: 'Failed to delete content' });
          },
        });
      },
    });
  }
}
