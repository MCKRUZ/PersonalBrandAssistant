import { Component, computed, inject, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { Select } from 'primeng/select';
import { Tag } from 'primeng/tag';
import { PageHeaderComponent, PageAction } from '../../shared/components/page-header/page-header.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { PlatformChipComponent } from '../../shared/components/platform-chip/platform-chip.component';
import { RelativeTimePipe } from '../../shared/pipes/relative-time.pipe';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Toast } from 'primeng/toast';
import { ContentPipelineDialogComponent } from './components/content-pipeline-dialog.component';
import { ContentStore } from './store/content.store';
import { ContentService } from './services/content.service';
import { ContentStatus, ContentType, Content } from '../../shared/models';
import { PLATFORM_ICONS, PLATFORM_COLORS, PLATFORM_LABELS } from '../../shared/utils/platform-icons';

@Component({
  selector: 'app-content-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TableModule, ButtonModule, Select, Tag, ConfirmDialog, Toast,
    PageHeaderComponent, EmptyStateComponent, LoadingSpinnerComponent,
    StatusBadgeComponent, PlatformChipComponent, RelativeTimePipe,
    ContentPipelineDialogComponent,
  ],
  template: `
    <app-content-pipeline-dialog #pipelineDialog (closed)="store.loadContent(store.filters())" />
    <app-page-header title="Content" [actions]="actions" />

    <div class="flex gap-3 mb-3">
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
    </div>

    @if (store.loading() && !store.hasContent()) {
      <app-loading-spinner message="Loading content..." />
    } @else if (!store.hasContent()) {
      <app-empty-state message="No content yet. Create your first piece!" icon="pi pi-file" />
    } @else {
      @for (group of groupedContent(); track group.platform) {
        <div class="platform-group">
          <div class="platform-group-header">
            <i [class]="group.icon" [style.color]="group.color"></i>
            <span>{{ group.label }}</span>
            <span class="group-count">{{ group.items.length }}</span>
          </div>
          <p-table [value]="$any(group.items)" [rowHover]="true" styleClass="p-datatable-sm">
            <ng-template #header>
              <tr>
                <th>Title</th>
                <th>Type</th>
                <th>Status</th>
                <th>Created</th>
                <th style="width: 6rem">Actions</th>
              </tr>
            </ng-template>
            <ng-template #body let-item>
              <tr class="cursor-pointer" (click)="viewContent(item)">
                <td>{{ item.title || 'Untitled' }}</td>
                <td><p-tag [value]="item.contentType" severity="info" /></td>
                <td><app-status-badge [status]="item.status" /></td>
                <td>{{ item.createdAt | relativeTime }}</td>
                <td>
                  <p-button icon="pi pi-eye" [text]="true" (onClick)="viewContent(item); $event.stopPropagation()" />
                  <p-button icon="pi pi-pencil" [text]="true" (onClick)="editContent(item); $event.stopPropagation()" />
                  <p-button icon="pi pi-trash" [text]="true" severity="danger" (onClick)="deleteContent(item); $event.stopPropagation()" />
                </td>
              </tr>
            </ng-template>
          </p-table>
        </div>
      }

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
    .platform-group { margin-bottom: 1.5rem; }
    .platform-group-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.9rem;
      font-weight: 700;
      margin-bottom: 0.5rem;
      padding: 0.5rem 0;
      border-bottom: 2px solid var(--p-surface-700, #25252f);
    }
    .platform-group-header i { font-size: 1.1rem; }
    .group-count {
      font-size: 0.75rem;
      font-weight: 600;
      background: var(--p-surface-700, #25252f);
      padding: 0.1rem 0.5rem;
      border-radius: 10px;
      color: var(--p-text-muted-color, #71717a);
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

  readonly groupedContent = computed(() => {
    const items = this.store.items();
    const groups = new Map<string, Content[]>();

    for (const item of items) {
      const platform = item.targetPlatforms?.[0] ?? 'Other';
      if (!groups.has(platform)) groups.set(platform, []);
      groups.get(platform)!.push(item);
    }

    return [...groups.entries()].map(([platform, groupItems]) => ({
      platform,
      label: (PLATFORM_LABELS as Record<string, string>)[platform] ?? platform,
      icon: (PLATFORM_ICONS as Record<string, string>)[platform] ?? 'pi pi-file',
      color: (PLATFORM_COLORS as Record<string, string>)[platform] ?? '#888',
      items: groupItems,
    }));
  });

  ngOnInit() {
    this.store.loadContent({});
  }

  applyFilters() {
    this.store.loadContent({ status: this.statusFilter, contentType: this.typeFilter });
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
            this.store.loadContent({ status: this.statusFilter, contentType: this.typeFilter });
          },
          error: () => {
            this.messageService.add({ severity: 'error', summary: 'Failed to delete content' });
          },
        });
      },
    });
  }
}
