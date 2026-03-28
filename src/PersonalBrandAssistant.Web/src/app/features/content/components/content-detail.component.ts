import { Component, inject, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { ConfirmationService, MessageService } from 'primeng/api';
import { PageHeaderComponent, PageAction } from '../../../shared/components/page-header/page-header.component';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';
import { PlatformChipComponent } from '../../../shared/components/platform-chip/platform-chip.component';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { ContentWorkflowPanelComponent } from './content-workflow-panel.component';
import { BrandVoicePanelComponent } from './brand-voice-panel.component';
import { ContentRepurposeDialogComponent } from './content-repurpose-dialog.component';
import { BlogWorkflowComponent } from './blog-workflow/blog-workflow.component';
import { ContentStore } from '../store/content.store';
import { ContentService } from '../services/content.service';
import { ContentStatus, ContentType, PlatformType } from '../../../shared/models';

@Component({
  selector: 'app-content-detail',
  standalone: true,
  imports: [
    CommonModule, ButtonModule, Card, Tag, ConfirmDialog,
    PageHeaderComponent, StatusBadgeComponent, PlatformChipComponent,
    LoadingSpinnerComponent, RelativeTimePipe, ContentWorkflowPanelComponent,
    BrandVoicePanelComponent, ContentRepurposeDialogComponent,
    BlogWorkflowComponent,
  ],
  providers: [ConfirmationService],
  template: `
    <p-confirmDialog />
    <app-content-repurpose-dialog #repurposeDialog (repurposed)="reload()" />

    @if (store.loading()) {
      <app-loading-spinner message="Loading content..." />
    } @else {
      @if (store.selectedContent(); as content) {
        <app-page-header [title]="content.title || 'Untitled'" [actions]="actions" />

        <div class="grid">
          <div class="col-12 md:col-8">
            <p-card header="Content">
              <div class="mb-3">
                <app-status-badge [status]="content.status" />
                <span class="ml-2 text-color-secondary">{{ content.contentType }}</span>
                <span class="ml-2 text-color-secondary">{{ content.createdAt | relativeTime }}</span>
              </div>

              <div class="flex gap-2 mb-3">
                @for (p of content.targetPlatforms; track p) {
                  <app-platform-chip [platform]="p" />
                }
              </div>

              @if (content.metadata.tags.length > 0) {
                <div class="flex gap-2 mb-3">
                  @for (tag of content.metadata.tags; track tag) {
                    <p-tag [value]="tag" severity="info" />
                  }
                </div>
              }

              <div class="content-body surface-ground p-3 border-round">
                <pre class="white-space-pre-wrap m-0">{{ content.body }}</pre>
              </div>
            </p-card>

            @if (content.contentType === 'BlogPost') {
              <div class="mt-3">
                <app-blog-workflow [content]="content" />
              </div>
            }
          </div>

          <div class="col-12 md:col-4 flex flex-column gap-3">
            <app-content-workflow-panel
              [currentStatus]="content.status"
              [allowedTransitions]="store.allowedTransitions()"
              [auditLog]="store.workflowLog()"
              (onTransition)="handleTransition($event)"
            />
            <app-brand-voice-panel [score]="store.brandVoiceScore()" />
          </div>
        </div>
      }
    }
  `,
  styles: `.content-body { font-family: monospace; line-height: 1.6; }`,
})
export class ContentDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly contentService = inject(ContentService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
  readonly store = inject(ContentStore);

  @ViewChild('repurposeDialog') repurposeDialog!: ContentRepurposeDialogComponent;

  actions: PageAction[] = [];

  ngOnInit() {
    const id = this.route.snapshot.params['id'];
    this.store.loadContentById(id);
    this.store.loadTransitions(id);
    this.store.loadBrandVoice(id);
    this.store.loadWorkflowLog(id);

    this.actions = [
      { label: 'Edit', icon: 'pi pi-pencil', command: () => this.router.navigate(['/content', id, 'edit']) },
      { label: 'Repurpose', icon: 'pi pi-copy', command: () => this.repurposeDialog.open(id) },
      {
        label: 'Delete', icon: 'pi pi-trash', command: () => {
          this.confirmationService.confirm({
            message: 'Are you sure you want to delete this content?',
            accept: () => {
              this.contentService.remove(id).subscribe(() => {
                this.messageService.add({ severity: 'success', summary: 'Deleted' });
                this.router.navigate(['/content']);
              });
            },
          });
        },
      },
    ];
  }

  handleTransition(targetStatus: ContentStatus) {
    const id = this.store.selectedContent()?.id;
    if (!id) return;
    this.contentService.transition(id, { targetStatus }).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Transitioned', detail: `Status changed to ${targetStatus}` });
        this.reload();
      },
    });
  }

  reload() {
    const id = this.route.snapshot.params['id'];
    this.store.loadContentById(id);
    this.store.loadTransitions(id);
    this.store.loadBrandVoice(id);
    this.store.loadWorkflowLog(id);
  }
}
