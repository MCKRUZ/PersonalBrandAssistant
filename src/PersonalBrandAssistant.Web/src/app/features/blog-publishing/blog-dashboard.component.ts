import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { Select } from 'primeng/select';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { ConfirmationService, MessageService } from 'primeng/api';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { BlogDashboardService } from './blog-dashboard.service';
import { BlogPipelineItem, DashboardStats } from './blog-dashboard.models';

@Component({
  selector: 'app-blog-dashboard',
  standalone: true,
  imports: [
    CommonModule, FormsModule, ButtonModule, Card, Tag, Select,
    ConfirmDialog, PageHeaderComponent, LoadingSpinnerComponent,
  ],
  providers: [ConfirmationService],
  template: `
    <p-confirmDialog />
    <app-page-header title="Blog Publishing" />

    <!-- Stats -->
    <div class="grid mb-4">
      <div class="col-4">
        <p-card>
          <div class="text-center">
            <div class="text-3xl font-bold text-primary">{{ stats().inPipeline }}</div>
            <div class="text-color-secondary mt-1">In Pipeline</div>
          </div>
        </p-card>
      </div>
      <div class="col-4">
        <p-card>
          <div class="text-center">
            <div class="text-3xl font-bold text-yellow-600">{{ stats().scheduled }}</div>
            <div class="text-color-secondary mt-1">Scheduled</div>
          </div>
        </p-card>
      </div>
      <div class="col-4">
        <p-card>
          <div class="text-center">
            <div class="text-3xl font-bold text-green-600">{{ stats().published }}</div>
            <div class="text-color-secondary mt-1">Published</div>
          </div>
        </p-card>
      </div>
    </div>

    <!-- Filters -->
    <div class="flex gap-3 mb-4">
      <p-select [options]="statusOptions" [(ngModel)]="selectedStatus"
                placeholder="Filter by status" [showClear]="true"
                (onChange)="loadItems()" />
    </div>

    <!-- Items -->
    @if (loading()) {
      <app-loading-spinner message="Loading blog pipeline..." />
    } @else if (items().length === 0) {
      <p-card>
        <div class="text-center text-color-secondary p-4">
          No blog posts in the pipeline yet.
        </div>
      </p-card>
    } @else {
      @for (item of items(); track item.id) {
        <p-card class="mb-3" [style]="{'cursor': 'pointer'}">
          <div class="flex align-items-center justify-content-between">
            <div class="flex-1">
              <div class="flex align-items-center gap-2 mb-2">
                <span class="font-semibold text-lg cursor-pointer text-primary"
                      (click)="navigateToContent(item.id)">
                  {{ item.title || 'Untitled' }}
                </span>
                @if (item.blogSkipped) {
                  <p-tag value="Skipped" severity="danger" />
                }
              </div>

              <!-- Timeline -->
              <div class="flex align-items-center gap-3 mb-2">
                <!-- Substack -->
                <div class="flex align-items-center gap-1">
                  <i class="pi pi-circle-fill text-sm"
                     [class]="item.substack?.status === 'Published' ? 'text-green-500' : 'text-gray-400'"></i>
                  <span class="text-sm">Substack</span>
                  <p-tag [value]="item.substack?.status ?? 'Pending'"
                         [severity]="platformSeverity(item.substack?.status)" size="small" />
                </div>

                <!-- Delay line -->
                <div class="flex-1 border-top-1 surface-border mx-2" style="height: 1px;"></div>
                <span class="text-xs text-color-secondary">
                  {{ item.blogDelayDays ? item.blogDelayDays + 'd' : '7d' }}
                  {{ item.blogDelayDays ? '(custom)' : '' }}
                </span>
                <div class="flex-1 border-top-1 surface-border mx-2" style="height: 1px;"></div>

                <!-- Blog -->
                <div class="flex align-items-center gap-1">
                  <i class="pi pi-circle-fill text-sm"
                     [class]="blogDotClass(item)"></i>
                  <span class="text-sm">Blog</span>
                  <p-tag [value]="blogStatusLabel(item)"
                         [severity]="blogStatusSeverity(item)" size="small" />
                </div>
              </div>
            </div>

            <!-- Actions -->
            <div class="flex gap-2">
              @if (!item.blogSkipped && item.substack?.status === 'Published' && !item.personalBlog?.publishedAt) {
                <button pButton label="Schedule" icon="pi pi-calendar" severity="info"
                        class="p-button-sm" (click)="schedule(item)"></button>
              }
              @if (!item.blogSkipped && !item.personalBlog?.publishedAt) {
                <button pButton label="Skip" icon="pi pi-ban" severity="secondary"
                        class="p-button-sm p-button-text"
                        (click)="skip(item)"></button>
              }
            </div>
          </div>
        </p-card>
      }
    }
  `
})
export class BlogDashboardComponent implements OnInit {
  private readonly service = inject(BlogDashboardService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly router = inject(Router);

  items = signal<BlogPipelineItem[]>([]);
  loading = signal(true);
  selectedStatus = '';

  statusOptions = [
    { label: 'All', value: '' },
    { label: 'Draft', value: 'Draft' },
    { label: 'Approved', value: 'Approved' },
    { label: 'Scheduled', value: 'Scheduled' },
    { label: 'Published', value: 'Published' },
  ];

  stats = computed<DashboardStats>(() => {
    const all = this.items();
    return {
      inPipeline: all.filter(i => !i.blogSkipped && i.personalBlog?.status !== 'Published').length,
      scheduled: all.filter(i => i.personalBlog?.scheduledAt && i.personalBlog?.status !== 'Published').length,
      published: all.filter(i => i.personalBlog?.status === 'Published').length,
    };
  });

  ngOnInit(): void {
    this.loadItems();
  }

  loadItems(): void {
    this.loading.set(true);
    const filter = this.selectedStatus ? { status: this.selectedStatus } : undefined;
    this.service.getItems(filter).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  schedule(item: BlogPipelineItem): void {
    this.service.schedule(item.id).subscribe({
      next: (res) => {
        this.messageService.add({
          severity: 'success',
          summary: 'Scheduled',
          detail: `Blog scheduled for ${res.scheduledAt}`
        });
        this.loadItems();
      },
      error: (err) => this.messageService.add({
        severity: 'error',
        summary: 'Error',
        detail: err?.error?.error ?? 'Failed to schedule'
      })
    });
  }

  skip(item: BlogPipelineItem): void {
    this.confirmationService.confirm({
      message: `Skip blog publishing for "${item.title}"?`,
      accept: () => {
        this.service.skipBlog(item.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'info', summary: 'Skipped', detail: 'Blog publishing skipped.' });
            this.loadItems();
          }
        });
      }
    });
  }

  navigateToContent(id: string): void {
    this.router.navigate(['/content', id]);
  }

  platformSeverity(status?: string): 'success' | 'info' | 'warning' | 'danger' | 'secondary' {
    switch (status) {
      case 'Published': return 'success';
      case 'Processing': return 'info';
      case 'Failed': return 'danger';
      default: return 'secondary';
    }
  }

  blogDotClass(item: BlogPipelineItem): string {
    if (item.blogSkipped) return 'text-red-400';
    if (item.personalBlog?.status === 'Published') return 'text-green-500';
    if (item.personalBlog?.scheduledAt) return 'text-yellow-500';
    return 'text-gray-400';
  }

  blogStatusLabel(item: BlogPipelineItem): string {
    if (item.blogSkipped) return 'Skipped';
    if (item.personalBlog?.status === 'Published') return 'Published';
    if (item.personalBlog?.scheduledAt) return 'Scheduled';
    return 'Pending';
  }

  blogStatusSeverity(item: BlogPipelineItem): 'success' | 'warning' | 'danger' | 'secondary' {
    if (item.blogSkipped) return 'danger';
    if (item.personalBlog?.status === 'Published') return 'success';
    if (item.personalBlog?.scheduledAt) return 'warning';
    return 'secondary';
  }
}
