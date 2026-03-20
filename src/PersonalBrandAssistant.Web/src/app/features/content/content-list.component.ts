import { Component, inject, OnInit, ViewChild } from '@angular/core';
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
import { ContentPipelineDialogComponent } from './components/content-pipeline-dialog.component';
import { ContentStore } from './store/content.store';
import { ContentStatus, ContentType, Content } from '../../shared/models';

@Component({
  selector: 'app-content-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TableModule, ButtonModule, Select, Tag,
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
      <p-table [value]="$any(store.items())" [rowHover]="true" styleClass="p-datatable-sm">
        <ng-template #header>
          <tr>
            <th>Title</th>
            <th>Type</th>
            <th>Status</th>
            <th>Platforms</th>
            <th>Created</th>
            <th style="width: 6rem">Actions</th>
          </tr>
        </ng-template>
        <ng-template #body let-item>
          <tr class="cursor-pointer" (click)="viewContent(item)">
            <td>{{ item.title || 'Untitled' }}</td>
            <td><p-tag [value]="item.contentType" severity="info" /></td>
            <td><app-status-badge [status]="item.status" /></td>
            <td>
              <div class="flex gap-1">
                @for (p of item.targetPlatforms; track p) {
                  <app-platform-chip [platform]="p" />
                }
              </div>
            </td>
            <td>{{ item.createdAt | relativeTime }}</td>
            <td>
              <p-button icon="pi pi-eye" [text]="true" (onClick)="viewContent(item); $event.stopPropagation()" />
              <p-button icon="pi pi-pencil" [text]="true" (onClick)="editContent(item); $event.stopPropagation()" />
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
  `,
  styles: `.cursor-pointer { cursor: pointer; }`,
})
export class ContentListComponent implements OnInit {
  private readonly router = inject(Router);
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
}
