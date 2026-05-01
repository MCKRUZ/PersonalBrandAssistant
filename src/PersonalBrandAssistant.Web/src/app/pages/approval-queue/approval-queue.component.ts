import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { Skeleton } from 'primeng/skeleton';
import { Chip } from 'primeng/chip';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { PlatformType } from '../../core/models/platform.model';
import { ApprovalStore } from './approval.store';
import { ApprovalApiService } from './approval-api.service';

@Component({
  selector: 'app-approval-queue',
  standalone: true,
  imports: [DatePipe, FormsModule, Button, Checkbox, Skeleton, Chip, StatusBadgeComponent],
  providers: [ApprovalStore, ApprovalApiService],
  templateUrl: './approval-queue.component.html',
  styleUrl: './approval-queue.component.scss',
})
export class ApprovalQueueComponent {
  protected readonly store = inject(ApprovalStore);

  protected readonly platforms: PlatformType[] = [
    'TwitterX', 'LinkedIn', 'Instagram', 'YouTube', 'Reddit', 'PersonalBlog', 'Substack',
  ];

  protected readonly expandedId = signal<string | null>(null);
  protected rejectFeedback = '';

  protected toggleExpand(id: string): void {
    const next = this.expandedId() === id ? null : id;
    this.rejectFeedback = '';
    this.expandedId.set(next);
  }

  protected isSelected(id: string): boolean {
    return this.store.selectedIds().includes(id);
  }

  protected onApprove(id: string, event: Event): void {
    event.stopPropagation();
    this.store.approve(id);
  }

  protected onRejectOpen(id: string, event: Event): void {
    event.stopPropagation();
    this.expandedId.set(id);
  }

  protected onReject(id: string): void {
    this.store.reject(id, this.rejectFeedback);
    this.rejectFeedback = '';
    this.expandedId.set(null);
  }

  protected onBatchApprove(): void {
    this.store.batchApprove();
  }

  protected isActiveFilter(platform: PlatformType | null): boolean {
    return this.store.platformFilter() === platform;
  }
}
