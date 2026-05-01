import { Component, OnInit, inject, DestroyRef, computed } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Button } from 'primeng/button';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { ContentEditorStore } from './content-editor.store';
import { ContentEditorApiService } from './content-editor-api.service';
import { BrandVoicePanelComponent } from './brand-voice-panel/brand-voice-panel.component';
import { PreviewTabComponent } from './tabs/preview-tab.component';
import { HistoryTabComponent } from './tabs/history-tab.component';
import { VersionsTabComponent } from './tabs/versions-tab.component';
import { DraftApplyService } from '../../shell/sidecar/draft-apply.service';
import { PlatformType } from '../../core/models/platform.model';
import { ContentType } from '../../core/models/content.model';

export const PLATFORM_CHAR_LIMITS: Readonly<Record<string, number | null>> = {
  TwitterX: 280,
  LinkedIn: 3000,
  Instagram: 2200,
  YouTube: 5000,
  Reddit: 40000,
  PersonalBlog: null,
  Substack: null,
};

@Component({
  selector: 'app-content-editor',
  standalone: true,
  imports: [
    FormsModule, Button, Select, Skeleton, Textarea,
    StatusBadgeComponent, BrandVoicePanelComponent,
    PreviewTabComponent, HistoryTabComponent, VersionsTabComponent,
  ],
  providers: [ContentEditorStore, ContentEditorApiService],
  templateUrl: './content-editor.component.html',
  styleUrl: './content-editor.component.scss',
})
export class ContentEditorComponent implements OnInit {
  readonly store = inject(ContentEditorStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly draftApply = inject(DraftApplyService);
  private readonly destroyRef = inject(DestroyRef);

  readonly platforms: { label: string; value: PlatformType }[] = [
    { label: 'LinkedIn', value: 'LinkedIn' },
    { label: 'Twitter/X', value: 'TwitterX' },
    { label: 'Instagram', value: 'Instagram' },
    { label: 'YouTube', value: 'YouTube' },
    { label: 'Reddit', value: 'Reddit' },
    { label: 'Personal Blog', value: 'PersonalBlog' },
    { label: 'Substack', value: 'Substack' },
  ];

  readonly contentTypes: { label: string; value: ContentType }[] = [
    { label: 'Social Post', value: 'SocialPost' },
    { label: 'Blog Post', value: 'BlogPost' },
    { label: 'Thread', value: 'Thread' },
    { label: 'Video Description', value: 'VideoDescription' },
  ];

  readonly charLimit = computed(() => {
    const content = this.store.content();
    if (!content) return null;
    return PLATFORM_CHAR_LIMITS[content.platform] ?? null;
  });

  readonly charCount = computed(() => {
    return this.store.content()?.body?.length ?? 0;
  });

  readonly charClass = computed(() => {
    const limit = this.charLimit();
    const count = this.charCount();
    if (!limit) return '';
    const ratio = count / limit;
    if (ratio > 1) return 'over-limit';
    if (ratio >= 0.9) return 'near-limit';
    return 'within-limit';
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.store.loadContent(id);
    }

    this.draftApply.apply$.pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((text) => {
      this.store.applyDraft(text);
    });
  }

  onTitleChange(title: string): void {
    this.store.updateField('title', title);
  }

  onBodyChange(body: string): void {
    this.store.updateField('body', body);
  }

  onPlatformChange(platform: PlatformType): void {
    this.store.updateField('platform', platform);
  }

  onTypeChange(type: ContentType): void {
    this.store.updateField('type', type);
  }

  navigateBack(): void {
    this.router.navigate(['/content']);
  }
}
