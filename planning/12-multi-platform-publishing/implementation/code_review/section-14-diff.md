diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.spec.ts
index a562f4a..5a38e73 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.spec.ts
@@ -18,16 +18,17 @@ function mockContent(overrides: Partial<ContentDetail> = {}): ContentDetail {
     status: ContentStatus.Draft,
     contentType: ContentType.BlogPost,
     primaryPlatform: Platform.Blog,
+    targetPlatforms: [Platform.Blog],
     voiceScore: 85,
     tags: ['angular'],
     createdAt: '2026-01-01T00:00:00Z',
     updatedAt: '2026-01-01T00:00:00Z',
     scheduledAt: null,
     publishedAt: null,
+    platformPublishes: [],
     viralityPrediction: null,
     sourceIdeaId: null,
     parentContentId: null,
-    platformPublishes: [],
     children: [],
     ...overrides,
   };
@@ -78,10 +79,12 @@ describe('ContentEditorComponent', () => {
       'create', 'get', 'update', 'delete', 'draft', 'crossPost',
       'approve', 'submitForReview', 'requestChanges', 'schedule',
       'unschedule', 'publish', 'unpublish', 'restore', 'voiceCheck',
+      'getPlatforms', 'getPublishStatus', 'retryPlatform',
     ]);
     contentService.create.and.returnValue(of('new-id-1'));
     contentService.approve.and.returnValue(of(void 0));
     contentService.draft.and.returnValue(of(void 0));
+    contentService.getPlatforms.and.returnValue(of([]));
 
     const mockSignalR = jasmine.createSpyObj('SignalRService',
       ['connect', 'disconnect', 'sendChatMessage'],
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.ts
index 126bcdc..f540064 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.ts
@@ -27,6 +27,9 @@ import {
   ContentType,
   Platform,
 } from '../models/content.model';
+import type { PlatformConnectionStatus } from '../models/content.model';
+import { PlatformTargetsComponent } from './platform-targets/platform-targets.component';
+import { PublishModalComponent } from './publish-modal/publish-modal.component';
 
 @Component({
   selector: 'app-content-editor',
@@ -46,6 +49,8 @@ import {
     MarkdownEditorComponent,
     EditorToolbarComponent,
     SidecarChatComponent,
+    PlatformTargetsComponent,
+    PublishModalComponent,
   ],
   template: `
     <div class="editor-page" data-testid="content-editor-page">
@@ -112,6 +117,15 @@ import {
         (draftAction)="onDraftAction($event)"
         (crossPostAction)="onCrossPostAction()" />
 
+      <app-platform-targets
+        [selectedPlatforms]="store.content()?.targetPlatforms ?? []"
+        [primaryPlatform]="store.content()?.primaryPlatform ?? Platform.Blog"
+        [connectedPlatforms]="connectedPlatforms()"
+        [bodyLength]="(store.content()?.body ?? '').length"
+        [wordCount]="wordCount()"
+        (targetPlatformsChange)="onTargetPlatformsChange($event)"
+        data-testid="platform-targets" />
+
       <p-splitter [style]="{ flex: 1, minHeight: 0 }" [panelSizes]="[50, 50]">
         <ng-template pTemplate>
           <app-markdown-editor
@@ -180,6 +194,16 @@ import {
         [visible]="chatPanelVisible()"
         (visibleChange)="chatPanelVisible.set($event)"
         [contentId]="store.content()?.id ?? ''" />
+
+      @if (store.content()) {
+        <app-publish-modal
+          [visible]="publishModalVisible()"
+          [content]="store.content()!"
+          [connectedPlatforms]="connectedPlatforms()"
+          [mode]="publishMode()"
+          (confirm)="onPublishConfirm($event)"
+          (cancel)="publishModalVisible.set(false)" />
+      }
     </div>
   `,
   styles: [`
@@ -268,12 +292,22 @@ export class ContentEditorComponent implements OnInit {
 
   readonly chatPanelVisible = signal(false);
   readonly isAiLoading = signal(false);
+  readonly publishModalVisible = signal(false);
+  readonly publishMode = signal<'publish' | 'schedule'>('publish');
+  readonly connectedPlatforms = signal<PlatformConnectionStatus[]>([]);
 
   private autoSaveTimer: ReturnType<typeof setTimeout> | null = null;
 
   readonly platformOptions = Object.values(Platform);
   readonly typeOptions = Object.values(ContentType);
 
+  readonly wordCount = computed(() => {
+    const body = this.store.content()?.body ?? '';
+    return body.trim() ? body.trim().split(/\s+/).length : 0;
+  });
+
+  readonly Platform = Platform;
+
   readonly canEdit = computed(() => {
     const status = this.store.content()?.status;
     return status === ContentStatus.Idea || status === ContentStatus.Draft || status === ContentStatus.Review;
@@ -318,6 +352,11 @@ export class ContentEditorComponent implements OnInit {
         });
     }
 
+    this.contentService.getPlatforms().subscribe({
+      next: (platforms) => this.connectedPlatforms.set(platforms),
+      error: () => this.connectedPlatforms.set([]),
+    });
+
     this.destroyRef.onDestroy(() => {
       if (this.autoSaveTimer) clearTimeout(this.autoSaveTimer);
       this.store.reset();
@@ -403,23 +442,38 @@ export class ContentEditorComponent implements OnInit {
   onApprove(): void { this.doStatusAction((id) => this.contentService.approve(id)); }
   onSubmitForReview(): void { this.doStatusAction((id) => this.contentService.submitForReview(id)); }
   onRequestChanges(): void { this.doStatusAction((id) => this.contentService.requestChanges(id)); }
-  onPublish(): void { this.doStatusAction((id) => this.contentService.publish(id)); }
   onUnpublish(): void { this.doStatusAction((id) => this.contentService.unpublish(id)); }
   onUnschedule(): void { this.doStatusAction((id) => this.contentService.unschedule(id)); }
   onRestore(): void { this.doStatusAction((id) => this.contentService.restore(id)); }
 
+  onPublish(): void {
+    this.publishMode.set('publish');
+    this.publishModalVisible.set(true);
+  }
+
   onSchedule(): void {
+    this.publishMode.set('schedule');
+    this.publishModalVisible.set(true);
+  }
+
+  onPublishConfirm(event: { platforms: Platform[]; scheduledAt?: string }): void {
     const id = this.store.content()?.id;
     if (!id) return;
-    const dateStr = prompt('Enter schedule date (ISO format, e.g. 2026-01-15T10:00:00Z):');
-    if (!dateStr) return;
-    if (isNaN(new Date(dateStr).getTime())) {
-      alert('Invalid date format. Please use ISO format (e.g. 2026-01-15T10:00:00Z).');
-      return;
+    this.publishModalVisible.set(false);
+    if (event.scheduledAt) {
+      this.contentService
+        .schedule(id, { scheduledAt: event.scheduledAt })
+        .subscribe(() => this.store.loadContent(id));
+    } else {
+      this.contentService
+        .publish(id, { targetPlatforms: event.platforms })
+        .subscribe(() => this.store.loadContent(id));
     }
-    this.contentService
-      .schedule(id, { scheduledAt: dateStr })
-      .subscribe(() => this.store.loadContent(id));
+  }
+
+  onTargetPlatformsChange(platforms: Platform[]): void {
+    this.store.updateField('targetPlatforms', platforms);
+    this.scheduleAutoSave();
   }
 
   private doStatusAction(action: (id: string) => import('rxjs').Observable<void>): void {
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/platform-targets/platform-targets.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/platform-targets/platform-targets.component.spec.ts
new file mode 100644
index 0000000..c8eaaaf
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/platform-targets/platform-targets.component.spec.ts
@@ -0,0 +1,164 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { PlatformTargetsComponent } from './platform-targets.component';
+import { Platform, PUBLISHABLE_PLATFORMS, PLATFORM_CHAR_LIMITS } from '../../models/content.model';
+import type { PlatformConnectionStatus } from '../../models/content.model';
+
+function makeConnection(platform: Platform, isConnected = true): PlatformConnectionStatus {
+  return {
+    platform,
+    isConnected,
+    isExpiring: false,
+    expiresAt: null,
+    capabilities: {
+      maxCharacters: PLATFORM_CHAR_LIMITS[platform] ?? 0,
+      supportsMarkdown: true,
+      supportsHtml: false,
+      supportsImages: true,
+      supportsScheduling: true,
+      supportsThreads: false,
+    },
+  };
+}
+
+describe('PlatformTargetsComponent', () => {
+  let component: PlatformTargetsComponent;
+  let fixture: ComponentFixture<PlatformTargetsComponent>;
+
+  const allConnected = PUBLISHABLE_PLATFORMS.map((p) => makeConnection(p));
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({
+      imports: [PlatformTargetsComponent],
+    });
+    fixture = TestBed.createComponent(PlatformTargetsComponent);
+    component = fixture.componentInstance;
+  });
+
+  it('should show checkboxes for all publishable platforms', () => {
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', allConnected);
+    fixture.componentRef.setInput('bodyLength', 100);
+    fixture.componentRef.setInput('wordCount', 20);
+    fixture.detectChanges();
+
+    const checkboxes = fixture.nativeElement.querySelectorAll('.platform-checkbox');
+    expect(checkboxes.length).toBe(PUBLISHABLE_PLATFORMS.length);
+  });
+
+  it('should disable checkbox for platforms that are not connected', () => {
+    const connections = [
+      makeConnection(Platform.Blog),
+      makeConnection(Platform.Medium, false),
+      makeConnection(Platform.Substack),
+      makeConnection(Platform.LinkedIn),
+      makeConnection(Platform.Twitter),
+    ];
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', connections);
+    fixture.componentRef.setInput('bodyLength', 100);
+    fixture.componentRef.setInput('wordCount', 20);
+    fixture.detectChanges();
+
+    const mediumCheckbox = fixture.nativeElement.querySelector('[data-platform="Medium"] input') as HTMLInputElement;
+    expect(mediumCheckbox.disabled).toBeTrue();
+  });
+
+  it('should pre-select platforms from selectedPlatforms', () => {
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog, Platform.LinkedIn]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', allConnected);
+    fixture.componentRef.setInput('bodyLength', 100);
+    fixture.componentRef.setInput('wordCount', 20);
+    fixture.detectChanges();
+
+    const linkedInCb = fixture.nativeElement.querySelector('[data-platform="LinkedIn"] input') as HTMLInputElement;
+    expect(linkedInCb.checked).toBeTrue();
+  });
+
+  it('should emit targetPlatformsChange when a platform is toggled on', () => {
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', allConnected);
+    fixture.componentRef.setInput('bodyLength', 100);
+    fixture.componentRef.setInput('wordCount', 20);
+    fixture.detectChanges();
+
+    let emitted: Platform[] | undefined;
+    component.targetPlatformsChange.subscribe((v: Platform[]) => (emitted = v));
+
+    const linkedInCb = fixture.nativeElement.querySelector('[data-platform="LinkedIn"] input') as HTMLInputElement;
+    linkedInCb.click();
+    fixture.detectChanges();
+
+    expect(emitted).toEqual([Platform.Blog, Platform.LinkedIn]);
+  });
+
+  it('should emit targetPlatformsChange when a platform is toggled off', () => {
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog, Platform.LinkedIn]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', allConnected);
+    fixture.componentRef.setInput('bodyLength', 100);
+    fixture.componentRef.setInput('wordCount', 20);
+    fixture.detectChanges();
+
+    let emitted: Platform[] | undefined;
+    component.targetPlatformsChange.subscribe((v: Platform[]) => (emitted = v));
+
+    const linkedInCb = fixture.nativeElement.querySelector('[data-platform="LinkedIn"] input') as HTMLInputElement;
+    linkedInCb.click();
+    fixture.detectChanges();
+
+    expect(emitted).toEqual([Platform.Blog]);
+  });
+
+  it('should always show primaryPlatform as checked and disabled', () => {
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', allConnected);
+    fixture.componentRef.setInput('bodyLength', 100);
+    fixture.componentRef.setInput('wordCount', 20);
+    fixture.detectChanges();
+
+    const blogCb = fixture.nativeElement.querySelector('[data-platform="Blog"] input') as HTMLInputElement;
+    expect(blogCb.checked).toBeTrue();
+    expect(blogCb.disabled).toBeTrue();
+  });
+
+  it('should show character count for Twitter', () => {
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog, Platform.Twitter]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', allConnected);
+    fixture.componentRef.setInput('bodyLength', 200);
+    fixture.componentRef.setInput('wordCount', 30);
+    fixture.detectChanges();
+
+    const twitterCount = fixture.nativeElement.querySelector('[data-platform="Twitter"] .char-count');
+    expect(twitterCount?.textContent).toContain('200/280');
+  });
+
+  it('should highlight platforms exceeding character limit', () => {
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog, Platform.Twitter]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', allConnected);
+    fixture.componentRef.setInput('bodyLength', 300);
+    fixture.componentRef.setInput('wordCount', 50);
+    fixture.detectChanges();
+
+    const twitterCount = fixture.nativeElement.querySelector('[data-platform="Twitter"] .char-count');
+    expect(twitterCount?.classList.contains('over-limit')).toBeTrue();
+  });
+
+  it('should show word count for platforms without character limits', () => {
+    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
+    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
+    fixture.componentRef.setInput('connectedPlatforms', allConnected);
+    fixture.componentRef.setInput('bodyLength', 500);
+    fixture.componentRef.setInput('wordCount', 80);
+    fixture.detectChanges();
+
+    const blogCount = fixture.nativeElement.querySelector('[data-platform="Blog"] .word-count');
+    expect(blogCount?.textContent).toContain('80 words');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/platform-targets/platform-targets.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/platform-targets/platform-targets.component.ts
new file mode 100644
index 0000000..4fd7998
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/platform-targets/platform-targets.component.ts
@@ -0,0 +1,92 @@
+import { Component, computed, input, output } from '@angular/core';
+import {
+  Platform,
+  PUBLISHABLE_PLATFORMS,
+  PLATFORM_CHAR_LIMITS,
+} from '../../models/content.model';
+import type { PlatformConnectionStatus } from '../../models/content.model';
+import { platformIconClass } from '../../content-list/content-display.utils';
+
+@Component({
+  selector: 'app-platform-targets',
+  standalone: true,
+  template: `
+    <div class="platform-targets">
+      @for (platform of platforms; track platform) {
+        <label class="platform-checkbox"
+               [attr.data-platform]="platform"
+               [class.disabled]="!isConnected(platform) || isPrimary(platform)">
+          <input type="checkbox"
+            [checked]="isSelected(platform)"
+            [disabled]="!isConnected(platform) || isPrimary(platform)"
+            (change)="togglePlatform(platform)" />
+          <i [class]="iconClass(platform)"></i>
+          <span class="platform-label">{{ platform }}</span>
+          @if (isSelected(platform) && charLimit(platform)) {
+            <span class="char-count" [class.over-limit]="bodyLength() > charLimit(platform)!">
+              {{ bodyLength() }}/{{ charLimit(platform) }}
+            </span>
+          }
+          @if (isSelected(platform) && !charLimit(platform)) {
+            <span class="word-count">{{ wordCount() }} words</span>
+          }
+        </label>
+      }
+    </div>
+  `,
+  styles: [`
+    .platform-targets {
+      display: flex; align-items: center; gap: 16px;
+      padding: 6px 16px; border-bottom: 1px solid #21262d; flex-shrink: 0;
+      overflow-x: auto;
+    }
+    .platform-checkbox {
+      display: flex; align-items: center; gap: 6px; font-size: 13px;
+      color: #c9d1d9; cursor: pointer; white-space: nowrap;
+    }
+    .platform-checkbox.disabled { opacity: 0.5; cursor: not-allowed; }
+    .platform-checkbox input { cursor: inherit; }
+    .platform-label { font-size: 12px; }
+    .char-count, .word-count { font-size: 11px; color: #8b949e; }
+    .char-count.over-limit { color: #f85149; font-weight: 600; }
+  `],
+})
+export class PlatformTargetsComponent {
+  readonly selectedPlatforms = input.required<Platform[]>();
+  readonly primaryPlatform = input.required<Platform>();
+  readonly connectedPlatforms = input.required<PlatformConnectionStatus[]>();
+  readonly bodyLength = input.required<number>();
+  readonly wordCount = input.required<number>();
+  readonly targetPlatformsChange = output<Platform[]>();
+
+  readonly platforms = PUBLISHABLE_PLATFORMS;
+  readonly iconClass = platformIconClass;
+
+  private readonly connectedSet = computed(() =>
+    new Set(this.connectedPlatforms().filter((c) => c.isConnected).map((c) => c.platform))
+  );
+
+  isConnected(platform: Platform): boolean {
+    return this.connectedSet().has(platform);
+  }
+
+  isPrimary(platform: Platform): boolean {
+    return platform === this.primaryPlatform();
+  }
+
+  isSelected(platform: Platform): boolean {
+    return this.isPrimary(platform) || this.selectedPlatforms().includes(platform);
+  }
+
+  charLimit(platform: Platform): number | undefined {
+    return PLATFORM_CHAR_LIMITS[platform];
+  }
+
+  togglePlatform(platform: Platform): void {
+    const current = this.selectedPlatforms();
+    const next = current.includes(platform)
+      ? current.filter((p) => p !== platform)
+      : [...current, platform];
+    this.targetPlatformsChange.emit(next);
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/publish-modal/publish-modal.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/publish-modal/publish-modal.component.spec.ts
new file mode 100644
index 0000000..e5fa099
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/publish-modal/publish-modal.component.spec.ts
@@ -0,0 +1,148 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { PublishModalComponent } from './publish-modal.component';
+import {
+  Platform,
+  ContentStatus,
+  ContentType,
+  PublishStatus,
+} from '../../models/content.model';
+import type { ContentDetail, PlatformConnectionStatus } from '../../models/content.model';
+
+function makeDetail(overrides: Partial<ContentDetail> = {}): ContentDetail {
+  return {
+    id: 'c-1',
+    title: 'Test Post',
+    body: 'Hello world content here.',
+    status: ContentStatus.Approved,
+    contentType: ContentType.BlogPost,
+    primaryPlatform: Platform.Blog,
+    targetPlatforms: [Platform.Blog, Platform.LinkedIn],
+    voiceScore: null,
+    tags: [],
+    createdAt: '2026-01-01T00:00:00Z',
+    updatedAt: '2026-01-01T00:00:00Z',
+    scheduledAt: null,
+    publishedAt: null,
+    platformPublishes: [],
+    viralityPrediction: null,
+    sourceIdeaId: null,
+    parentContentId: null,
+    children: [],
+    ...overrides,
+  };
+}
+
+function makeConnection(platform: Platform, connected = true): PlatformConnectionStatus {
+  return {
+    platform,
+    isConnected: connected,
+    isExpiring: false,
+    expiresAt: null,
+    capabilities: {
+      maxCharacters: 0,
+      supportsMarkdown: true,
+      supportsHtml: false,
+      supportsImages: true,
+      supportsScheduling: true,
+      supportsThreads: false,
+    },
+  };
+}
+
+describe('PublishModalComponent', () => {
+  let component: PublishModalComponent;
+  let fixture: ComponentFixture<PublishModalComponent>;
+
+  const connections = [
+    makeConnection(Platform.Blog),
+    makeConnection(Platform.LinkedIn),
+    makeConnection(Platform.Medium, false),
+    makeConnection(Platform.Twitter),
+    makeConnection(Platform.Substack),
+  ];
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({
+      imports: [PublishModalComponent],
+    });
+    fixture = TestBed.createComponent(PublishModalComponent);
+    component = fixture.componentInstance;
+  });
+
+  function setupModal(overrides: Partial<ContentDetail> = {}) {
+    fixture.componentRef.setInput('visible', true);
+    fixture.componentRef.setInput('content', makeDetail(overrides));
+    fixture.componentRef.setInput('connectedPlatforms', connections);
+    fixture.componentRef.setInput('mode', 'publish');
+    fixture.detectChanges();
+  }
+
+  it('should show primary platform prominently at top', () => {
+    setupModal();
+    const primary = fixture.nativeElement.querySelector('[data-testid="primary-platform"]');
+    expect(primary).toBeTruthy();
+    expect(primary.textContent).toContain('Blog');
+  });
+
+  it('should show connection status per platform', () => {
+    setupModal();
+    const badges = fixture.nativeElement.querySelectorAll('.connection-status');
+    expect(badges.length).toBeGreaterThan(0);
+  });
+
+  it('should allow toggling secondary platforms', () => {
+    setupModal({ targetPlatforms: [Platform.Blog, Platform.LinkedIn, Platform.Twitter] });
+    const twitterCheckbox = fixture.nativeElement.querySelector('[data-platform="Twitter"] input') as HTMLInputElement;
+    expect(twitterCheckbox).toBeTruthy();
+    expect(twitterCheckbox.disabled).toBeFalse();
+  });
+
+  it('should not allow deselecting the primary platform', () => {
+    setupModal();
+    const primaryCheckbox = fixture.nativeElement.querySelector('[data-testid="primary-platform"] input') as HTMLInputElement;
+    expect(primaryCheckbox.disabled).toBeTrue();
+    expect(primaryCheckbox.checked).toBeTrue();
+  });
+
+  it('should emit confirm with selected platforms', () => {
+    setupModal({ targetPlatforms: [Platform.Blog, Platform.LinkedIn] });
+    let emitted: { platforms: Platform[]; scheduledAt?: string } | undefined;
+    component.confirm.subscribe((v: { platforms: Platform[]; scheduledAt?: string }) => (emitted = v));
+
+    const confirmBtn = fixture.nativeElement.querySelector('[data-testid="confirm-btn"]') as HTMLButtonElement;
+    confirmBtn.click();
+
+    expect(emitted).toBeTruthy();
+    expect(emitted!.platforms).toContain(Platform.Blog);
+    expect(emitted!.platforms).toContain(Platform.LinkedIn);
+  });
+
+  it('should keep confirm button enabled when only primary platform is selected', () => {
+    setupModal({ targetPlatforms: [] });
+    fixture.detectChanges();
+    const confirmBtn = fixture.nativeElement.querySelector('[data-testid="confirm-btn"]') as HTMLButtonElement;
+    expect(confirmBtn.disabled).toBeFalse();
+  });
+
+  it('should emit cancel when cancel button clicked', () => {
+    setupModal();
+    let cancelled = false;
+    component.cancel.subscribe(() => (cancelled = true));
+
+    const cancelBtn = fixture.nativeElement.querySelector('[data-testid="cancel-btn"]') as HTMLButtonElement;
+    cancelBtn.click();
+
+    expect(cancelled).toBeTrue();
+  });
+
+  it('should show Schedule header when mode is schedule', () => {
+    fixture.componentRef.setInput('visible', true);
+    fixture.componentRef.setInput('content', makeDetail());
+    fixture.componentRef.setInput('connectedPlatforms', connections);
+    fixture.componentRef.setInput('mode', 'schedule');
+    fixture.detectChanges();
+
+    const header = fixture.nativeElement.querySelector('.modal-header');
+    expect(header?.textContent).toContain('Schedule');
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/publish-modal/publish-modal.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/publish-modal/publish-modal.component.ts
new file mode 100644
index 0000000..f146ae3
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/publish-modal/publish-modal.component.ts
@@ -0,0 +1,211 @@
+import { Component, input, output, signal, computed } from '@angular/core';
+import {
+  Platform,
+  PUBLISHABLE_PLATFORMS,
+  PLATFORM_CHAR_LIMITS,
+} from '../../models/content.model';
+import type { ContentDetail, PlatformConnectionStatus } from '../../models/content.model';
+import { platformIconClass } from '../../content-list/content-display.utils';
+
+@Component({
+  selector: 'app-publish-modal',
+  standalone: true,
+  template: `
+    @if (visible()) {
+      <div class="modal-backdrop" (click)="onCancel()">
+        <div class="modal-content" (click)="$event.stopPropagation()">
+          <h3 class="modal-header">
+            {{ mode() === 'schedule' ? 'Schedule to Platforms' : 'Publish to Platforms' }}
+          </h3>
+
+          <div class="platform-list">
+            <div class="platform-row primary" data-testid="primary-platform">
+              <label>
+                <input type="checkbox" [checked]="true" [disabled]="true" />
+                <i [class]="iconClass(content().primaryPlatform)"></i>
+                <span class="platform-name">{{ content().primaryPlatform }}</span>
+                <span class="primary-badge">Primary</span>
+              </label>
+              <span class="connection-status" [class]="connectionClass(content().primaryPlatform)">
+                {{ connectionLabel(content().primaryPlatform) }}
+              </span>
+            </div>
+
+            @for (platform of secondaryPlatforms(); track platform) {
+              <div class="platform-row" [attr.data-platform]="platform">
+                <label>
+                  <input type="checkbox"
+                    [checked]="isSelected(platform)"
+                    [disabled]="!isConnected(platform)"
+                    (change)="togglePlatform(platform)" />
+                  <i [class]="iconClass(platform)"></i>
+                  <span class="platform-name">{{ platform }}</span>
+                  @if (charLimit(platform) && isSelected(platform)) {
+                    <span class="char-info"
+                          [class.over-limit]="(content().body?.length ?? 0) > charLimit(platform)!">
+                      {{ content().body?.length ?? 0 }}/{{ charLimit(platform) }}
+                    </span>
+                  }
+                </label>
+                <span class="connection-status" [class]="connectionClass(platform)">
+                  {{ connectionLabel(platform) }}
+                </span>
+              </div>
+            }
+          </div>
+
+          @if (mode() === 'schedule') {
+            <div class="schedule-picker">
+              <label>
+                Schedule for:
+                <input type="datetime-local" [value]="scheduledAt()"
+                       (input)="scheduledAt.set(asInputValue($event))"
+                       data-testid="schedule-input" />
+              </label>
+            </div>
+          }
+
+          <div class="modal-actions">
+            <button class="btn-cancel" data-testid="cancel-btn" (click)="onCancel()">Cancel</button>
+            <button class="btn-confirm" data-testid="confirm-btn"
+                    [disabled]="selectedPlatforms().length === 0"
+                    (click)="onConfirm()">
+              {{ mode() === 'schedule' ? 'Schedule' : 'Publish' }}
+            </button>
+          </div>
+        </div>
+      </div>
+    }
+  `,
+  styles: [`
+    .modal-backdrop {
+      position: fixed; inset: 0; background: rgba(0, 0, 0, 0.6);
+      display: flex; align-items: center; justify-content: center; z-index: 1000;
+    }
+    .modal-content {
+      background: #161b22; border: 1px solid #30363d; border-radius: 12px;
+      padding: 24px; width: 480px; max-width: 90vw; max-height: 80vh; overflow-y: auto;
+    }
+    .modal-header {
+      margin: 0 0 16px; font-size: 18px; color: #f0f6fc; font-weight: 600;
+    }
+    .platform-list { display: flex; flex-direction: column; gap: 8px; margin-bottom: 16px; }
+    .platform-row {
+      display: flex; align-items: center; justify-content: space-between;
+      padding: 8px 12px; border-radius: 8px; background: #0d1117;
+    }
+    .platform-row.primary { border: 1px solid #c8715640; }
+    .platform-row label {
+      display: flex; align-items: center; gap: 8px; cursor: pointer; color: #c9d1d9; font-size: 14px;
+    }
+    .platform-name { font-weight: 500; }
+    .primary-badge {
+      font-size: 11px; background: #c8715633; color: #c87156;
+      padding: 2px 8px; border-radius: 10px;
+    }
+    .connection-status { font-size: 12px; }
+    .connection-status.connected { color: #3fb950; }
+    .connection-status.disconnected { color: #f85149; }
+    .connection-status.expiring { color: #d29922; }
+    .char-info { font-size: 11px; color: #8b949e; }
+    .char-info.over-limit { color: #f85149; font-weight: 600; }
+    .schedule-picker { margin-bottom: 16px; }
+    .schedule-picker label { color: #c9d1d9; font-size: 14px; display: flex; align-items: center; gap: 8px; }
+    .schedule-picker input {
+      background: #0d1117; border: 1px solid #30363d; border-radius: 6px;
+      padding: 6px 10px; color: #f0f6fc; font-family: inherit;
+    }
+    .modal-actions { display: flex; justify-content: flex-end; gap: 8px; }
+    .btn-cancel {
+      background: transparent; border: 1px solid #30363d; color: #c9d1d9;
+      border-radius: 6px; padding: 8px 16px; cursor: pointer; font-family: inherit; font-size: 14px;
+    }
+    .btn-confirm {
+      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
+      padding: 8px 20px; cursor: pointer; font-family: inherit; font-size: 14px; font-weight: 500;
+    }
+    .btn-confirm:disabled { opacity: 0.5; cursor: not-allowed; }
+    .btn-confirm:hover:not(:disabled) { background: #d4836a; }
+  `],
+})
+export class PublishModalComponent {
+  readonly visible = input.required<boolean>();
+  readonly content = input.required<ContentDetail>();
+  readonly connectedPlatforms = input.required<PlatformConnectionStatus[]>();
+  readonly mode = input<'publish' | 'schedule'>('publish');
+
+  readonly visibleChange = output<boolean>();
+  readonly confirm = output<{ platforms: Platform[]; scheduledAt?: string }>();
+  readonly cancel = output<void>();
+
+  readonly scheduledAt = signal('');
+  readonly iconClass = platformIconClass;
+
+  private readonly selected = signal<Platform[]>([]);
+
+  readonly secondaryPlatforms = computed(() =>
+    PUBLISHABLE_PLATFORMS.filter((p) => p !== this.content().primaryPlatform)
+  );
+
+  readonly selectedPlatforms = computed(() => {
+    const primary = this.content().primaryPlatform;
+    const secondary = this.selected().length > 0
+      ? this.selected()
+      : (this.content().targetPlatforms ?? []).filter((p) => p !== primary);
+    return [primary, ...secondary];
+  });
+
+  isSelected(platform: Platform): boolean {
+    return this.selectedPlatforms().includes(platform);
+  }
+
+  isConnected(platform: Platform): boolean {
+    return this.connectedPlatforms().some((c) => c.platform === platform && c.isConnected);
+  }
+
+  connectionClass(platform: Platform): string {
+    const conn = this.connectedPlatforms().find((c) => c.platform === platform);
+    if (!conn || !conn.isConnected) return 'disconnected';
+    if (conn.isExpiring) return 'expiring';
+    return 'connected';
+  }
+
+  connectionLabel(platform: Platform): string {
+    const conn = this.connectedPlatforms().find((c) => c.platform === platform);
+    if (!conn || !conn.isConnected) return 'Not connected';
+    if (conn.isExpiring) return 'Expiring';
+    return 'Connected';
+  }
+
+  charLimit(platform: Platform): number | undefined {
+    return PLATFORM_CHAR_LIMITS[platform];
+  }
+
+  togglePlatform(platform: Platform): void {
+    const current = this.selected().length > 0
+      ? this.selected()
+      : (this.content().targetPlatforms ?? []).filter((p) => p !== this.content().primaryPlatform);
+    const next = current.includes(platform)
+      ? current.filter((p) => p !== platform)
+      : [...current, platform];
+    this.selected.set(next);
+  }
+
+  asInputValue(event: Event): string {
+    return (event.target as HTMLInputElement).value;
+  }
+
+  onConfirm(): void {
+    const result: { platforms: Platform[]; scheduledAt?: string } = {
+      platforms: this.selectedPlatforms(),
+    };
+    if (this.mode() === 'schedule' && this.scheduledAt()) {
+      result.scheduledAt = new Date(this.scheduledAt()).toISOString();
+    }
+    this.confirm.emit(result);
+  }
+
+  onCancel(): void {
+    this.cancel.emit();
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat/sidecar-chat.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat/sidecar-chat.component.spec.ts
index ac4a7b8..592dff2 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat/sidecar-chat.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/sidecar-chat/sidecar-chat.component.spec.ts
@@ -16,16 +16,17 @@ function mockContent(overrides: Partial<ContentDetail> = {}): ContentDetail {
     status: ContentStatus.Draft,
     contentType: ContentType.BlogPost,
     primaryPlatform: Platform.Blog,
+    targetPlatforms: [Platform.Blog],
     voiceScore: 85,
     tags: ['angular'],
     createdAt: '2026-01-01T00:00:00Z',
     updatedAt: '2026-01-01T00:00:00Z',
     scheduledAt: null,
     publishedAt: null,
+    platformPublishes: [],
     viralityPrediction: null,
     sourceIdeaId: null,
     parentContentId: null,
-    platformPublishes: [],
     children: [],
     ...overrides,
   };
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.spec.ts
index 8c819d3..1b3cdf1 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.spec.ts
@@ -1,6 +1,6 @@
 import { ComponentFixture, TestBed } from '@angular/core/testing';
 import { ContentCardComponent } from './content-card.component';
-import { ContentStatus, ContentType, Platform } from '../../models/content.model';
+import { ContentStatus, ContentType, Platform, PublishStatus } from '../../models/content.model';
 import type { Content } from '../../models/content.model';
 
 describe('ContentCardComponent', () => {
@@ -13,12 +13,14 @@ describe('ContentCardComponent', () => {
     contentType: ContentType.BlogPost,
     status: ContentStatus.Draft,
     primaryPlatform: Platform.Blog,
+    targetPlatforms: [Platform.Blog],
     voiceScore: 85,
     tags: ['angular', 'typescript', 'ai', 'extra'],
     createdAt: '2026-01-01T00:00:00Z',
     updatedAt: '2026-01-01T00:00:00Z',
     scheduledAt: null,
     publishedAt: null,
+    platformPublishes: [],
   };
 
   beforeEach(() => {
@@ -103,4 +105,66 @@ describe('ContentCardComponent', () => {
     btn.click();
     expect(component.duplicate.emit).toHaveBeenCalledWith('content-1');
   });
+
+  it('should show green badge for Published platform status', () => {
+    fixture.componentRef.setInput('content', {
+      ...mockContent,
+      platformPublishes: [
+        { platform: Platform.Blog, publishStatus: PublishStatus.Published, publishedUrl: 'https://example.com/1' },
+      ],
+    });
+    fixture.detectChanges();
+    const badge = fixture.nativeElement.querySelector('[data-status="Published"]');
+    expect(badge).toBeTruthy();
+  });
+
+  it('should show red badge with retry button for Failed platform status', () => {
+    fixture.componentRef.setInput('content', {
+      ...mockContent,
+      platformPublishes: [
+        { platform: Platform.LinkedIn, publishStatus: PublishStatus.Failed, publishedUrl: null },
+      ],
+    });
+    fixture.detectChanges();
+    const badge = fixture.nativeElement.querySelector('[data-status="Failed"]');
+    expect(badge).toBeTruthy();
+    const retryBtn = fixture.nativeElement.querySelector('[data-testid="retry-btn"]');
+    expect(retryBtn).toBeTruthy();
+  });
+
+  it('should show pending badge for Pending platform status', () => {
+    fixture.componentRef.setInput('content', {
+      ...mockContent,
+      platformPublishes: [
+        { platform: Platform.Blog, publishStatus: PublishStatus.Pending, publishedUrl: null },
+      ],
+    });
+    fixture.detectChanges();
+    const badge = fixture.nativeElement.querySelector('[data-status="Pending"]');
+    expect(badge).toBeTruthy();
+  });
+
+  it('should not show badges when platformPublishes is empty', () => {
+    fixture.componentRef.setInput('content', { ...mockContent, platformPublishes: [] });
+    fixture.detectChanges();
+    expect(fixture.nativeElement.querySelector('[data-testid="publish-badges"]')).toBeNull();
+  });
+
+  it('should emit retry event when retry button clicked', () => {
+    fixture.componentRef.setInput('content', {
+      ...mockContent,
+      platformPublishes: [
+        { platform: Platform.LinkedIn, publishStatus: PublishStatus.Failed, publishedUrl: null },
+      ],
+    });
+    fixture.detectChanges();
+
+    let emitted: Platform | undefined;
+    component.retry.subscribe((v: Platform) => (emitted = v));
+
+    const retryBtn = fixture.nativeElement.querySelector('[data-testid="retry-btn"]');
+    retryBtn.click();
+
+    expect(emitted).toBe(Platform.LinkedIn);
+  });
 });
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.ts
index 21088db..0b239a2 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.ts
@@ -2,8 +2,8 @@ import { Component, input, output } from '@angular/core';
 import { DatePipe } from '@angular/common';
 import { ButtonModule } from 'primeng/button';
 import { TagModule } from 'primeng/tag';
-import { Content } from '../../models/content.model';
-import { formatContentType, voiceScoreClass, platformIconClass, truncateText } from '../content-display.utils';
+import { Content, Platform } from '../../models/content.model';
+import { formatContentType, voiceScoreClass, platformIconClass, truncateText, publishStatusSeverity } from '../content-display.utils';
 
 @Component({
   selector: 'app-content-card',
@@ -26,6 +26,22 @@ import { formatContentType, voiceScoreClass, platformIconClass, truncateText } f
         <div class="card-meta">
           <span class="updated-at">{{ content().updatedAt | date: 'shortDate' }}</span>
         </div>
+        @if (content().platformPublishes?.length > 0) {
+          <div class="publish-badges" data-testid="publish-badges">
+            @for (pub of content().platformPublishes; track pub.platform) {
+              <span class="pub-badge" [attr.data-status]="pub.publishStatus"
+                    [attr.data-platform]="pub.platform">
+                <i [class]="platformIcon(pub.platform)"></i>
+                @if (pub.publishStatus === 'Failed') {
+                  <button class="retry-btn" (click)="retry.emit(pub.platform); $event.stopPropagation()"
+                          data-testid="retry-btn">
+                    <i class="pi pi-refresh"></i>
+                  </button>
+                }
+              </span>
+            }
+          </div>
+        }
         @if (content().tags.length > 0) {
           <div class="card-tags">
             @for (tag of content().tags.slice(0, 3); track tag) {
@@ -142,6 +158,28 @@ import { formatContentType, voiceScoreClass, platformIconClass, truncateText } f
         color: #8b949e;
         align-self: center;
       }
+      .publish-badges {
+        display: flex;
+        gap: 4px;
+        flex-wrap: wrap;
+        margin-bottom: 8px;
+      }
+      .pub-badge {
+        display: inline-flex;
+        align-items: center;
+        gap: 4px;
+        padding: 2px 6px;
+        border-radius: 4px;
+        font-size: 12px;
+      }
+      .pub-badge[data-status='Published'] { background: #2ea04333; color: #2ea043; }
+      .pub-badge[data-status='Failed'] { background: #f8514933; color: #f85149; }
+      .pub-badge[data-status='Pending'],
+      .pub-badge[data-status='Formatting'] { background: #d2992233; color: #d29922; }
+      .retry-btn {
+        background: none; border: none; color: inherit; cursor: pointer;
+        padding: 0; font-size: 11px; line-height: 1;
+      }
       .card-actions {
         display: flex;
         gap: 4px;
@@ -156,9 +194,11 @@ export class ContentCardComponent {
   readonly edit = output<string>();
   readonly onDelete = output<string>();
   readonly duplicate = output<string>();
+  readonly retry = output<Platform>();
 
   readonly truncate = truncateText;
   readonly platformIcon = platformIconClass;
   readonly formatType = formatContentType;
   readonly voiceClass = voiceScoreClass;
+  readonly pubSeverity = publishStatusSeverity;
 }
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-display.utils.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-display.utils.ts
index 0a1276d..392eadf 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-display.utils.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-display.utils.ts
@@ -12,6 +12,7 @@ export function voiceScoreClass(score: number | null): string {
 export function platformIconClass(platform: string): string {
   const icons: Record<string, string> = {
     Blog: 'pi pi-globe',
+    Medium: 'pi pi-book',
     LinkedIn: 'pi pi-linkedin',
     Twitter: 'pi pi-twitter',
     Substack: 'pi pi-envelope',
@@ -24,3 +25,13 @@ export function platformIconClass(platform: string): string {
 export function truncateText(text: string, maxLength: number): string {
   return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
 }
+
+export function publishStatusSeverity(status: string): 'success' | 'danger' | 'warn' | 'info' {
+  switch (status) {
+    case 'Published': return 'success';
+    case 'Failed': return 'danger';
+    case 'Pending':
+    case 'Formatting': return 'warn';
+    default: return 'info';
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-list.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-list.component.spec.ts
index 823f980..ae55af3 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-list.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-list.component.spec.ts
@@ -22,12 +22,14 @@ describe('ContentListComponent', () => {
     contentType: ContentType.BlogPost,
     status: ContentStatus.Draft,
     primaryPlatform: Platform.Blog,
+    targetPlatforms: [Platform.Blog],
     voiceScore: 85,
     tags: ['angular'],
     createdAt: '2026-01-01T00:00:00Z',
     updatedAt: '2026-01-01T00:00:00Z',
     scheduledAt: null,
     publishedAt: null,
+    platformPublishes: [],
   };
 
   const emptyPage: PagedResult<Content> = {
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/models/content.model.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/models/content.model.ts
index 0384a1f..4c3bd73 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/models/content.model.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/models/content.model.ts
@@ -21,6 +21,7 @@ export enum ContentType {
 
 export enum Platform {
   Blog = 'Blog',
+  Medium = 'Medium',
   Substack = 'Substack',
   LinkedIn = 'LinkedIn',
   Twitter = 'Twitter',
@@ -35,18 +36,33 @@ export enum PublishStatus {
   Failed = 'Failed',
 }
 
+export const PUBLISHABLE_PLATFORMS: Platform[] = [
+  Platform.Blog,
+  Platform.Medium,
+  Platform.Substack,
+  Platform.LinkedIn,
+  Platform.Twitter,
+];
+
+export const PLATFORM_CHAR_LIMITS: Partial<Record<Platform, number>> = {
+  [Platform.Twitter]: 280,
+  [Platform.LinkedIn]: 3000,
+};
+
 export interface Content {
   id: string;
   title: string;
   contentType: ContentType;
   status: ContentStatus;
   primaryPlatform: Platform;
+  targetPlatforms: Platform[];
   voiceScore: number | null;
   tags: string[];
   createdAt: string;
   updatedAt: string;
   scheduledAt: string | null;
   publishedAt: string | null;
+  platformPublishes: PlatformPublishSummary[];
 }
 
 export interface ContentDetail extends Content {
@@ -64,6 +80,14 @@ export interface PlatformPublish {
   publishStatus: PublishStatus;
   publishedUrl: string | null;
   publishedAt: string | null;
+  retryCount: number;
+  nextRetryAt: string | null;
+}
+
+export interface PlatformPublishSummary {
+  platform: Platform;
+  publishStatus: PublishStatus;
+  publishedUrl: string | null;
 }
 
 export interface ChildContent {
@@ -86,6 +110,7 @@ export interface CreateContentRequest {
   primaryPlatform: Platform;
   sourceIdeaId?: string;
   tags: string[];
+  targetPlatforms?: Platform[];
 }
 
 export interface UpdateContentRequest {
@@ -94,6 +119,7 @@ export interface UpdateContentRequest {
   tags?: string[];
   contentType?: ContentType;
   primaryPlatform?: Platform;
+  targetPlatforms?: Platform[];
   lastUpdatedAt: string;
 }
 
@@ -111,6 +137,33 @@ export interface CrossPostRequest {
   targetPlatform: Platform;
 }
 
+export interface PublishRequest {
+  targetPlatforms?: Platform[];
+}
+
+export interface PublishStatusResponse {
+  contentId: string;
+  primaryPlatform: Platform;
+  platformStatuses: PlatformPublish[];
+}
+
+export interface PlatformConnectionStatus {
+  platform: Platform;
+  isConnected: boolean;
+  isExpiring: boolean;
+  expiresAt: string | null;
+  capabilities: PlatformCapabilities;
+}
+
+export interface PlatformCapabilities {
+  maxCharacters: number;
+  supportsMarkdown: boolean;
+  supportsHtml: boolean;
+  supportsImages: boolean;
+  supportsScheduling: boolean;
+  supportsThreads: boolean;
+}
+
 export interface ContentFilterState {
   status?: ContentStatus;
   platform?: Platform;
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.spec.ts
index 9e3ccb6..ecf9032 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.spec.ts
@@ -8,8 +8,13 @@ import {
   ContentStatus,
   ContentType,
   Platform,
+  PublishStatus,
   VoiceCheckResult,
 } from '../models/content.model';
+import type {
+  PublishStatusResponse,
+  PlatformConnectionStatus,
+} from '../models/content.model';
 import { PagedResult } from '../../../models/pagination.model';
 
 describe('ContentService', () => {
@@ -75,17 +80,18 @@ describe('ContentService', () => {
       contentType: ContentType.BlogPost,
       status: ContentStatus.Draft,
       primaryPlatform: Platform.Blog,
+      targetPlatforms: [Platform.Blog],
       voiceScore: null,
       tags: [],
       createdAt: '2026-01-01T00:00:00Z',
       updatedAt: '2026-01-01T00:00:00Z',
       scheduledAt: null,
       publishedAt: null,
+      platformPublishes: [],
       body: '',
       viralityPrediction: null,
       sourceIdeaId: null,
       parentContentId: null,
-      platformPublishes: [],
       children: [],
     };
 
@@ -216,13 +222,25 @@ describe('ContentService', () => {
     req.flush(null);
   });
 
-  it('publish() calls POST /api/content/{id}/publish', () => {
+  it('publish() calls POST /api/content/{id}/publish with empty body when no request given', () => {
     const id = '123e4567-e89b-12d3-a456-426614174000';
 
     service.publish(id).subscribe();
 
     const req = httpMock.expectOne(`/api/content/${id}/publish`);
     expect(req.request.method).toBe('POST');
+    expect(req.request.body).toEqual({});
+    req.flush(null);
+  });
+
+  it('publish() sends targetPlatforms in request body', () => {
+    const id = '123e4567-e89b-12d3-a456-426614174000';
+
+    service.publish(id, { targetPlatforms: [Platform.Blog, Platform.LinkedIn] }).subscribe();
+
+    const req = httpMock.expectOne(`/api/content/${id}/publish`);
+    expect(req.request.method).toBe('POST');
+    expect(req.request.body).toEqual({ targetPlatforms: [Platform.Blog, Platform.LinkedIn] });
     req.flush(null);
   });
 
@@ -258,4 +276,68 @@ describe('ContentService', () => {
     expect(req.request.method).toBe('GET');
     req.flush(mockResult);
   });
+
+  it('getPublishStatus() calls GET /api/content/{id}/publish-status', () => {
+    const id = '123e4567-e89b-12d3-a456-426614174000';
+    const mockResponse: PublishStatusResponse = {
+      contentId: id,
+      primaryPlatform: Platform.Blog,
+      platformStatuses: [
+        {
+          id: 'pub-1',
+          platform: Platform.Blog,
+          publishStatus: PublishStatus.Published,
+          publishedUrl: 'https://matthewkruczek.ai/post-1',
+          publishedAt: '2026-05-27T12:00:00Z',
+          retryCount: 0,
+          nextRetryAt: null,
+        },
+      ],
+    };
+
+    service.getPublishStatus(id).subscribe((result) => {
+      expect(result).toEqual(mockResponse);
+    });
+
+    const req = httpMock.expectOne(`/api/content/${id}/publish-status`);
+    expect(req.request.method).toBe('GET');
+    req.flush(mockResponse);
+  });
+
+  it('retryPlatform() calls POST /api/content/{id}/retry/{platform}', () => {
+    const id = '123e4567-e89b-12d3-a456-426614174000';
+
+    service.retryPlatform(id, Platform.LinkedIn).subscribe();
+
+    const req = httpMock.expectOne(`/api/content/${id}/retry/LinkedIn`);
+    expect(req.request.method).toBe('POST');
+    req.flush(null);
+  });
+
+  it('getPlatforms() calls GET /api/platforms', () => {
+    const mockPlatforms: PlatformConnectionStatus[] = [
+      {
+        platform: Platform.Blog,
+        isConnected: true,
+        isExpiring: false,
+        expiresAt: null,
+        capabilities: {
+          maxCharacters: 0,
+          supportsMarkdown: true,
+          supportsHtml: true,
+          supportsImages: true,
+          supportsScheduling: true,
+          supportsThreads: false,
+        },
+      },
+    ];
+
+    service.getPlatforms().subscribe((result) => {
+      expect(result).toEqual(mockPlatforms);
+    });
+
+    const req = httpMock.expectOne('/api/platforms');
+    expect(req.request.method).toBe('GET');
+    req.flush(mockPlatforms);
+  });
 });
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.ts
index 5719704..11f2c9a 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.ts
@@ -11,6 +11,12 @@ import {
   ScheduleContentRequest,
   CrossPostRequest,
   VoiceCheckResult,
+  Platform,
+} from '../models/content.model';
+import type {
+  PublishRequest,
+  PublishStatusResponse,
+  PlatformConnectionStatus,
 } from '../models/content.model';
 import { PagedResult } from '../../../models/pagination.model';
 
@@ -83,8 +89,8 @@ export class ContentService {
     return this.http.put<void>(`${this.baseUrl}/${id}/unschedule`, {});
   }
 
-  publish(id: string): Observable<void> {
-    return this.http.post<void>(`${this.baseUrl}/${id}/publish`, {});
+  publish(id: string, request?: PublishRequest): Observable<void> {
+    return this.http.post<void>(`${this.baseUrl}/${id}/publish`, request ?? {});
   }
 
   unpublish(id: string): Observable<void> {
@@ -98,4 +104,16 @@ export class ContentService {
   voiceCheck(id: string): Observable<VoiceCheckResult> {
     return this.http.get<VoiceCheckResult>(`${this.baseUrl}/${id}/voice-check`);
   }
+
+  getPublishStatus(id: string): Observable<PublishStatusResponse> {
+    return this.http.get<PublishStatusResponse>(`${this.baseUrl}/${id}/publish-status`);
+  }
+
+  retryPlatform(id: string, platform: Platform): Observable<void> {
+    return this.http.post<void>(`${this.baseUrl}/${id}/retry/${platform}`, {});
+  }
+
+  getPlatforms(): Observable<PlatformConnectionStatus[]> {
+    return this.http.get<PlatformConnectionStatus[]>('/api/platforms');
+  }
 }
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content-editor.store.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content-editor.store.spec.ts
index c78a027..c00139e 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content-editor.store.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content-editor.store.spec.ts
@@ -20,6 +20,7 @@ describe('ContentEditorStore', () => {
     contentType: ContentType.BlogPost,
     status: ContentStatus.Draft,
     primaryPlatform: Platform.Blog,
+    targetPlatforms: [Platform.Blog],
     voiceScore: 0.85,
     viralityPrediction: null,
     sourceIdeaId: null,
@@ -92,6 +93,7 @@ describe('ContentEditorStore', () => {
       tags: ['test'],
       contentType: ContentType.BlogPost,
       primaryPlatform: Platform.Blog,
+      targetPlatforms: [Platform.Blog],
       lastUpdatedAt: '2026-01-01T12:00:00Z',
     });
   });
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content-editor.store.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content-editor.store.ts
index 86572ab..b317500 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content-editor.store.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content-editor.store.ts
@@ -108,6 +108,7 @@ export const ContentEditorStore = signalStore(
             tags: content.tags,
             contentType: content.contentType,
             primaryPlatform: content.primaryPlatform,
+            targetPlatforms: content.targetPlatforms,
             lastUpdatedAt: content.updatedAt,
           })
           .subscribe({
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content.store.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content.store.spec.ts
index da2b4c5..630d70f 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content.store.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/content/stores/content.store.spec.ts
@@ -24,12 +24,14 @@ describe('ContentStore', () => {
     contentType: ContentType.BlogPost,
     status: ContentStatus.Draft,
     primaryPlatform: Platform.Blog,
+    targetPlatforms: [Platform.Blog],
     voiceScore: null,
     tags: ['test'],
     createdAt: '2026-01-01T00:00:00Z',
     updatedAt: '2026-01-01T00:00:00Z',
     scheduledAt: null,
     publishedAt: null,
+    platformPublishes: [],
   };
 
   beforeEach(() => {
