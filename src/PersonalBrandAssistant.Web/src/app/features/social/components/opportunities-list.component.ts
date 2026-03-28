import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { SelectModule } from 'primeng/select';
import { DialogModule } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { SelectButtonModule } from 'primeng/selectbutton';
import { Tooltip } from 'primeng/tooltip';
import { SocialStore } from '../store/social.store';
import { DiscoveredOpportunity } from '../models/social.model';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-opportunities-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, ButtonModule, TagModule, SelectModule,
    DialogModule, TextareaModule, ToggleSwitchModule, SelectButtonModule, Tooltip,
    EmptyStateComponent, LoadingSpinnerComponent,
  ],
  template: `
    <div class="opportunities-toolbar">
      <p-button
        label="Discover"
        icon="pi pi-search"
        [loading]="store.discovering()"
        (onClick)="discover()"
        pTooltip="Find engagement opportunities from your monitored sources"
      />
      <p-select
        [options]="platformOptions"
        [(ngModel)]="platformFilter"
        placeholder="All Platforms"
        [showClear]="true"
        [style]="{width: '180px'}"
        pTooltip="Filter opportunities by platform"
      />
      <p-selectbutton
        [options]="impactOptions"
        [(ngModel)]="impactFilter"
        optionLabel="label"
        optionValue="value"
      />
      <div class="flex-1"></div>
      <div class="saved-toggle">
        <label>Show Saved</label>
        <p-toggleswitch [(ngModel)]="showSaved" (ngModelChange)="onSavedToggle()" />
      </div>
    </div>

    @if (store.discovering()) {
      <app-loading-spinner />
    } @else if (showSaved) {
      @if (store.savedOpportunities().length === 0) {
        <app-empty-state
          icon="pi pi-bookmark"
          title="No saved opportunities"
          message="Save opportunities from the discovery list to review later."
        />
      } @else {
        <div class="opportunity-cards">
          @for (opp of store.savedOpportunities(); track opp.postUrl) {
            <div class="opportunity-card">
              <div class="card-header">
                <p-tag [value]="opp.platform" [severity]="getPlatformSeverity(opp.platform)" size="small" />
                @if (opp.community) {
                  <span class="community-label">{{ opp.community }}</span>
                }
              </div>
              @if (opp.title) {
                <h4 class="card-title">{{ opp.title | slice:0:100 }}</h4>
              }
              @if (opp.contentPreview) {
                <p class="card-preview">{{ opp.contentPreview | slice:0:200 }}</p>
              }
              <div class="card-actions">
                <p-button
                  icon="pi pi-comments"
                  label="Engage"
                  severity="success"
                  size="small"
                  [loading]="store.engaging()"
                  (onClick)="openEngageDialog(opp)"
                  pTooltip="Generate an AI comment and post it"
                />
                <p-button
                  icon="pi pi-times"
                  label="Remove"
                  severity="secondary"
                  size="small"
                  [text]="true"
                  (onClick)="dismiss(opp)"
                  pTooltip="Remove from saved"
                />
              </div>
            </div>
          }
        </div>
      }
    } @else if (!store.hasOpportunities()) {
      @if (store.hasDiscovered()) {
        <app-empty-state
          icon="pi pi-info-circle"
          title="No opportunities found"
          message="No recent trend items matched your interest keywords. Make sure the trend monitor has sources configured."
        />
      } @else {
        <app-empty-state
          icon="pi pi-compass"
          title="No opportunities yet"
          message="Click Discover to surface engagement opportunities from your monitored sources."
        />
      }
    } @else {
      <div class="opportunity-cards">
        @for (opp of filteredOpportunities(); track opp.postUrl) {
          <div class="opportunity-card">
            <div class="card-header">
              <p-tag [value]="opp.platform" [severity]="getPlatformSeverity(opp.platform)" size="small" />
              @if (opp.impactScore) {
                <p-tag [value]="opp.impactScore" [severity]="getImpactSeverity(opp.impactScore)" size="small" />
              }
              @if (opp.category && opp.category !== 'General') {
                <p-tag [value]="opp.category" severity="info" [rounded]="true" size="small" />
              }
              @if (opp.community) {
                <span class="community-label">{{ opp.community }}</span>
              }
            </div>
            <h4 class="card-title">{{ opp.title | slice:0:100 }}</h4>
            <p class="card-preview">{{ opp.contentPreview | slice:0:200 }}</p>
            <div class="card-actions">
              <p-button
                icon="pi pi-comments"
                label="Engage"
                severity="success"
                size="small"
                [loading]="store.engaging()"
                (onClick)="openEngageDialog(opp)"
                pTooltip="Generate an AI comment and post it"
              />
              <p-button
                icon="pi pi-bookmark"
                label="Save"
                severity="info"
                size="small"
                [text]="true"
                (onClick)="save(opp)"
                pTooltip="Save this opportunity for later"
              />
              <p-button
                icon="pi pi-times"
                label="Dismiss"
                severity="secondary"
                size="small"
                [text]="true"
                (onClick)="dismiss(opp)"
                pTooltip="Remove from suggestions"
              />
            </div>
          </div>
        }
      </div>
    }

    <p-dialog
      header="Confirm Engagement"
      [(visible)]="showEngageDialog"
      [modal]="true"
      [style]="{width: '550px'}"
    >
      @if (selectedOpportunity) {
        <div class="engage-dialog-content">
          <p class="engage-target">
            <strong>{{ selectedOpportunity.platform }}</strong> — {{ selectedOpportunity.title | slice:0:80 }}
          </p>
          <p class="engage-info">An AI comment will be generated and posted. You can edit it before confirming.</p>
          <textarea
            pTextarea
            [(ngModel)]="editableComment"
            [rows]="5"
            placeholder="AI comment will appear here after posting..."
            class="w-full"
          ></textarea>
          <div class="dialog-actions">
            <p-button label="Cancel" [text]="true" (onClick)="showEngageDialog = false" />
            <p-button
              label="Post Comment"
              icon="pi pi-send"
              severity="success"
              [loading]="store.engaging()"
              (onClick)="confirmEngage()"
            />
          </div>
        </div>
      }
    </p-dialog>
  `,
  styles: [`
    .opportunities-toolbar {
      display: flex;
      gap: 0.5rem;
      align-items: center;
      margin-bottom: 1rem;
    }
    .flex-1 { flex: 1; }
    .saved-toggle {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.875rem;
    }
    .opportunity-cards {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .opportunity-card {
      border: 1px solid var(--surface-200);
      border-radius: 8px;
      padding: 1rem;
    }
    .card-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.5rem;
    }
    .community-label {
      font-size: 0.8rem;
      color: var(--text-color-secondary);
    }
    .card-title {
      margin: 0 0 0.25rem 0;
      font-size: 0.95rem;
      font-weight: 600;
    }
    .card-preview {
      margin: 0 0 0.75rem 0;
      font-size: 0.85rem;
      color: var(--text-color-secondary);
      line-height: 1.4;
    }
    .card-actions {
      display: flex;
      gap: 0.5rem;
    }
    .engage-dialog-content {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .engage-target {
      margin: 0;
      font-size: 0.9rem;
    }
    .engage-info {
      margin: 0;
      font-size: 0.85rem;
      color: var(--text-color-secondary);
    }
    .dialog-actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
      padding-top: 0.5rem;
    }
    .w-full { width: 100%; }
  `],
})
export class OpportunitiesListComponent {
  readonly store = inject(SocialStore);

  platformOptions = ['Reddit', 'TwitterX', 'LinkedIn', 'Instagram'];
  impactOptions = [
    { label: 'All', value: 'All' },
    { label: 'High Impact', value: 'High' },
  ];
  platformFilter: string | null = null;
  impactFilter = 'All';
  showSaved = false;
  showEngageDialog = false;
  selectedOpportunity: DiscoveredOpportunity | null = null;
  editableComment = '';

  filteredOpportunities() {
    let opps = this.store.opportunities();
    if (this.platformFilter) {
      opps = opps.filter(o => o.platform === this.platformFilter);
    }
    if (this.impactFilter && this.impactFilter !== 'All') {
      opps = opps.filter(o => o.impactScore === this.impactFilter);
    }
    return opps;
  }

  getImpactSeverity(impact: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    const map: Record<string, 'danger' | 'warn' | 'secondary'> = {
      High: 'danger',
      Medium: 'warn',
      Low: 'secondary',
    };
    return map[impact] ?? 'secondary';
  }

  getPlatformSeverity(platform: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    const map: Record<string, 'success' | 'info' | 'warn' | 'danger' | 'secondary'> = {
      Reddit: 'danger',
      TwitterX: 'info',
      LinkedIn: 'success',
      Instagram: 'warn',
    };
    return map[platform] ?? 'secondary';
  }

  discover() {
    this.store.discoverOpportunities();
  }

  onSavedToggle() {
    if (this.showSaved) {
      this.store.loadSaved();
    }
  }

  openEngageDialog(opp: DiscoveredOpportunity) {
    this.selectedOpportunity = opp;
    this.editableComment = '';
    this.showEngageDialog = true;
  }

  confirmEngage() {
    if (!this.selectedOpportunity) return;
    const opp = this.selectedOpportunity;
    this.store.engageSingle({
      platform: opp.platform,
      postId: opp.postId,
      postUrl: opp.postUrl,
      title: opp.title,
      content: opp.contentPreview,
      community: opp.community,
    });
    this.showEngageDialog = false;
  }

  save(opp: DiscoveredOpportunity) {
    this.store.saveOpportunity(opp);
  }

  dismiss(opp: DiscoveredOpportunity) {
    this.store.dismissOpportunity(opp);
  }
}
