import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { Toast } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { SettingsStore } from './settings.store';
import { AutonomyDialComponent } from './autonomy-dial/autonomy-dial.component';
import { BrandVoiceConfigComponent } from './brand-voice-config/brand-voice-config.component';
import { QuickPromptsEditorComponent } from './quick-prompts-editor/quick-prompts-editor.component';
import { AutonomySettings } from '../../core/models/autonomy.model';
import { BrandProfile } from './brand-profile.model';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [
    CommonModule, RouterLink, CardModule, ButtonModule, Toast,
    PageHeaderComponent, LoadingSpinnerComponent,
    AutonomyDialComponent, BrandVoiceConfigComponent, QuickPromptsEditorComponent,
  ],
  providers: [MessageService],
  template: `
    <app-page-header title="Settings" />
    <p-toast />

    @if (store.loading()) {
      <app-loading-spinner message="Loading settings..." />
    } @else {
      <div class="settings-stack">
        <app-brand-voice-config [profile]="store.brandProfile()" (profileChange)="onBrandVoiceSave($event)" />
        <app-autonomy-dial [autonomy]="store.autonomy()" (autonomyChange)="onAutonomySave($event)" />
        <app-quick-prompts-editor [prompts]="store.quickPrompts()" (promptsChange)="onPromptsSave($event)" (promptsReset)="onPromptsReset()" />

        <p-card>
          <div class="platform-link">
            <div>
              <h4>Platform Connections</h4>
              <p>Manage OAuth connections to your social platforms.</p>
            </div>
            <a routerLink="/platforms">
              <p-button label="Manage Platforms" icon="pi pi-external-link" [text]="true" />
            </a>
          </div>
        </p-card>
      </div>
    }
  `,
  styles: `
    .settings-stack { display: flex; flex-direction: column; gap: 1.5rem; }
    .platform-link { display: flex; align-items: center; justify-content: space-between; }
    .platform-link h4 { margin: 0 0 0.25rem; font-weight: 600; }
    .platform-link p { margin: 0; color: var(--p-text-muted-color); }
  `,
})
export class SettingsComponent implements OnInit {
  readonly store = inject(SettingsStore);
  private readonly messageService = inject(MessageService);

  ngOnInit() {
    this.store.loadAutonomy(undefined);
    this.store.loadBrandProfile();
    this.store.loadQuickPrompts();
  }

  onAutonomySave(settings: AutonomySettings) {
    this.store.saveAutonomy(
      settings,
      () => this.messageService.add({ severity: 'success', summary: 'Autonomy settings saved' }),
      () => this.messageService.add({ severity: 'error', summary: 'Failed to save autonomy settings' }),
    );
  }

  onBrandVoiceSave(profile: BrandProfile) {
    this.store.updateBrandProfile(profile);
    this.messageService.add({ severity: 'success', summary: 'Brand voice settings saved' });
  }

  onPromptsSave(prompts: Record<string, string[]>) {
    for (const [key, value] of Object.entries(prompts)) {
      this.store.updateQuickPrompts(key, value);
    }
    this.messageService.add({ severity: 'success', summary: 'Quick prompts saved' });
  }

  onPromptsReset() {
    this.store.resetQuickPrompts();
    this.messageService.add({ severity: 'info', summary: 'Quick prompts reset to defaults' });
  }
}
