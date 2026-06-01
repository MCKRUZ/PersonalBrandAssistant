diff --git a/src/PersonalBrandAssistant.Web/src/app/app.routes.ts b/src/PersonalBrandAssistant.Web/src/app/app.routes.ts
index 2d85a6f..b93fa39 100644
--- a/src/PersonalBrandAssistant.Web/src/app/app.routes.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/app.routes.ts
@@ -14,7 +14,7 @@ export const routes: Routes = [
       { path: 'calendar', loadComponent: () => import('./features/calendar/calendar.component').then(m => m.CalendarComponent) },
       { path: 'analytics', loadComponent: () => import('./features/analytics/analytics.component').then(m => m.AnalyticsComponent) },
       { path: 'listening', loadComponent: () => import('./features/listening/listening.component').then(m => m.ListeningComponent) },
-      { path: 'settings', loadComponent: () => import('./features/settings/settings.component').then(m => m.SettingsComponent) },
+      { path: 'settings', loadChildren: () => import('./features/settings/settings.routes').then(m => m.SETTINGS_ROUTES) },
     ]
   }
 ];
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.spec.ts
index 8263758..18609ca 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-card/idea-card.component.spec.ts
@@ -17,6 +17,8 @@ describe('IdeaCardComponent', () => {
     tags: ['angular', 'typescript', 'testing', 'extra-tag'],
     detectedAt: '2026-01-15T10:00:00Z',
     hasSavedDetails: false,
+    description: null,
+    url: null,
   };
 
   beforeEach(async () => {
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-grid/idea-grid.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-grid/idea-grid.component.spec.ts
index b82adac..c82fd30 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-grid/idea-grid.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-grid/idea-grid.component.spec.ts
@@ -18,6 +18,8 @@ describe('IdeaGridComponent', () => {
       tags: [],
       detectedAt: '2026-01-01T00:00:00Z',
       hasSavedDetails: false,
+      description: null,
+      url: null,
     },
     {
       id: 'idea-2',
@@ -30,6 +32,8 @@ describe('IdeaGridComponent', () => {
       tags: [],
       detectedAt: '2026-01-02T00:00:00Z',
       hasSavedDetails: false,
+      description: null,
+      url: null,
     },
   ];
 
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-list/idea-list.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-list/idea-list.component.spec.ts
index 324fad6..42fd6e5 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-list/idea-list.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/idea-list/idea-list.component.spec.ts
@@ -21,6 +21,8 @@ describe('IdeaListComponent', () => {
       tags: [],
       detectedAt: '2026-01-01T00:00:00Z',
       hasSavedDetails: false,
+      description: null,
+      url: null,
     },
   ];
 
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/save-idea-dialog/save-idea-dialog.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/save-idea-dialog/save-idea-dialog.component.spec.ts
index 6831649..694f593 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/save-idea-dialog/save-idea-dialog.component.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/components/save-idea-dialog/save-idea-dialog.component.spec.ts
@@ -21,6 +21,8 @@ describe('SaveIdeaDialogComponent', () => {
     tags: ['existing-tag'],
     detectedAt: '2026-01-01T00:00:00Z',
     hasSavedDetails: false,
+    description: null,
+    url: null,
   };
 
   beforeEach(async () => {
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.spec.ts
index 6ea6f9b..435d645 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.spec.ts
@@ -29,6 +29,8 @@ describe('IdeaStore', () => {
     tags: [],
     detectedAt: '2026-01-01T00:00:00Z',
     hasSavedDetails: false,
+    description: null,
+    url: null,
   };
 
   beforeEach(() => {
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/news/store/news-dismiss.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/news/store/news-dismiss.spec.ts
index 97a3642..ead3b25 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/news/store/news-dismiss.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/news/store/news-dismiss.spec.ts
@@ -3,33 +3,41 @@ import { provideHttpClient } from '@angular/common/http';
 import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
 import { MessageService } from 'primeng/api';
 import { NewsStore } from './news.store';
-import { environment } from '../../../environments/environment';
+import { IdeaStatus } from '../../../models/idea.model';
+import type { Idea } from '../../../models/idea.model';
+import type { PagedResult } from '../../../models/pagination.model';
 
 describe('NewsStore dismiss', () => {
   let store: InstanceType<typeof NewsStore>;
   let httpMock: HttpTestingController;
 
-  const makeSuggestion = (id: string, title: string) => ({
+  const makeIdea = (id: string, title: string): Idea => ({
     id,
-    topic: title,
-    rationale: 'test',
-    relevanceScore: 0.8,
-    suggestedContentType: 'SocialPost',
-    suggestedPlatforms: ['LinkedIn'],
-    createdAt: new Date().toISOString(),
-    status: 'Pending',
-    relatedTrends: [{
-      trendItemId: `ti-${id}`,
-      source: 'TestSource',
-      sourceName: 'Test',
-      title,
-      description: 'desc',
-      url: 'https://example.com',
-      score: 0.8,
-      sourceCategory: 'AI/ML',
-    }],
+    title,
+    sourceName: 'TestSource',
+    category: 'AI/ML',
+    summary: 'desc',
+    thumbnailUrl: null,
+    status: IdeaStatus.New,
+    tags: [],
+    detectedAt: new Date().toISOString(),
+    hasSavedDetails: false,
+    description: null,
+    url: `https://example.com/${id}`,
   });
 
+  const flushIdeasLoad = (ideas: Idea[]) => {
+    const page: PagedResult<Idea> = {
+      items: ideas,
+      totalCount: ideas.length,
+      page: 1,
+      pageSize: 5000,
+      totalPages: 1,
+    };
+    const req = httpMock.expectOne(r => r.url.includes('/api/ideas'));
+    req.flush(page);
+  };
+
   beforeEach(() => {
     TestBed.configureTestingModule({
       providers: [
@@ -47,106 +55,94 @@ describe('NewsStore dismiss', () => {
     httpMock.verify();
   });
 
-  it('should have 3 suggestions initially after manual state set', () => {
-    // Manually load suggestions into the store
-    const suggestions = [
-      makeSuggestion('aaa', 'First Story'),
-      makeSuggestion('bbb', 'Second Story'),
-      makeSuggestion('ccc', 'Third Story'),
+  it('should have 3 items after load', () => {
+    const ideas = [
+      makeIdea('aaa', 'First Story'),
+      makeIdea('bbb', 'Second Story'),
+      makeIdea('ccc', 'Third Story'),
     ];
 
-    // Use load and intercept the HTTP call to inject test data
     store.load(undefined);
-    const req = httpMock.expectOne(r => r.url.includes('trends/suggestions'));
-    req.flush(suggestions);
+    flushIdeasLoad(ideas);
 
-    expect(store.suggestions().length).toBe(3);
+    expect(store.items().length).toBe(3);
     expect(store.filteredItems().length).toBe(3);
     expect(store.groupedByCategory().length).toBeGreaterThan(0);
   });
 
-  it('should remove the dismissed suggestion from state immediately', fakeAsync(() => {
-    const suggestions = [
-      makeSuggestion('aaa', 'First Story'),
-      makeSuggestion('bbb', 'Second Story'),
-      makeSuggestion('ccc', 'Third Story'),
+  it('should remove the dismissed item from state immediately', fakeAsync(() => {
+    const ideas = [
+      makeIdea('aaa', 'First Story'),
+      makeIdea('bbb', 'Second Story'),
+      makeIdea('ccc', 'Third Story'),
     ];
 
     store.load(undefined);
-    const loadReq = httpMock.expectOne(r => r.url.includes('trends/suggestions'));
-    loadReq.flush(suggestions);
+    flushIdeasLoad(ideas);
     tick();
 
-    expect(store.suggestions().length).toBe(3);
+    expect(store.items().length).toBe(3);
 
-    // Dismiss the first item — feedItemId format is "suggestionId-trendIndex"
-    store.dismiss('aaa-0');
+    store.dismiss('aaa');
     tick();
 
-    // Should immediately drop to 2 suggestions
-    expect(store.suggestions().length).toBe(2);
-    expect(store.suggestions().find(s => s.id === 'aaa')).toBeUndefined();
+    expect(store.items().length).toBe(2);
+    expect(store.items().find(i => i.id === 'aaa')).toBeUndefined();
     expect(store.filteredItems().length).toBe(2);
 
-    // The API call should be in-flight
-    const dismissReq = httpMock.expectOne(r => r.url.includes('trends/suggestions/aaa/dismiss'));
-    expect(dismissReq.request.method).toBe('POST');
+    const dismissReq = httpMock.expectOne(r => r.url.includes('/api/ideas/aaa/dismiss'));
+    expect(dismissReq.request.method).toBe('PUT');
     dismissReq.flush(null);
     tick();
 
-    // Still 2 after API success
-    expect(store.suggestions().length).toBe(2);
+    expect(store.items().length).toBe(2);
   }));
 
   it('should rollback on API error', fakeAsync(() => {
-    const suggestions = [
-      makeSuggestion('aaa', 'First Story'),
-      makeSuggestion('bbb', 'Second Story'),
+    const ideas = [
+      makeIdea('aaa', 'First Story'),
+      makeIdea('bbb', 'Second Story'),
     ];
 
     store.load(undefined);
-    httpMock.expectOne(r => r.url.includes('trends/suggestions')).flush(suggestions);
+    flushIdeasLoad(ideas);
     tick();
 
-    store.dismiss('aaa-0');
+    store.dismiss('aaa');
     tick();
 
-    expect(store.suggestions().length).toBe(1);
+    expect(store.items().length).toBe(1);
 
-    // API fails
-    const dismissReq = httpMock.expectOne(r => r.url.includes('trends/suggestions/aaa/dismiss'));
+    const dismissReq = httpMock.expectOne(r => r.url.includes('/api/ideas/aaa/dismiss'));
     dismissReq.flush('error', { status: 500, statusText: 'Server Error' });
     tick();
 
-    // Should rollback
-    expect(store.suggestions().length).toBe(2);
+    expect(store.items().length).toBe(2);
   }));
 
   it('should update groupedByCategory after dismiss', fakeAsync(() => {
-    const suggestions = [
-      makeSuggestion('aaa', 'First Story'),
-      makeSuggestion('bbb', 'Second Story'),
-      makeSuggestion('ccc', 'Third Story'),
+    const ideas = [
+      makeIdea('aaa', 'First Story'),
+      makeIdea('bbb', 'Second Story'),
+      makeIdea('ccc', 'Third Story'),
     ];
 
     store.load(undefined);
-    httpMock.expectOne(r => r.url.includes('trends/suggestions')).flush(suggestions);
+    flushIdeasLoad(ideas);
     tick();
 
-    const groupBefore = store.groupedByCategory();
-    const itemsBefore = groupBefore.flatMap(g => g.items);
+    const itemsBefore = store.groupedByCategory().flatMap(g => g.items);
     expect(itemsBefore.length).toBe(3);
 
-    store.dismiss('aaa-0');
+    store.dismiss('aaa');
     tick();
 
-    const groupAfter = store.groupedByCategory();
-    const itemsAfter = groupAfter.flatMap(g => g.items);
+    const itemsAfter = store.groupedByCategory().flatMap(g => g.items);
     expect(itemsAfter.length).toBe(2);
     expect(itemsAfter.find(i => i.title === 'First Story')).toBeUndefined();
     expect(itemsAfter[0].title).toBe('Second Story');
 
-    httpMock.expectOne(r => r.url.includes('dismiss')).flush(null);
+    httpMock.expectOne(r => r.url.includes('/api/ideas/aaa/dismiss')).flush(null);
     tick();
   }));
 });
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/general/general-settings.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/general/general-settings.component.ts
new file mode 100644
index 0000000..fac47d8
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/general/general-settings.component.ts
@@ -0,0 +1,16 @@
+import { Component } from '@angular/core';
+
+@Component({
+  selector: 'app-general-settings',
+  standalone: true,
+  template: `
+    <div class="general">
+      <p class="placeholder">General settings coming soon.</p>
+    </div>
+  `,
+  styles: [`
+    .general { padding: 8px 0; }
+    .placeholder { color: #8a8a96; font-size: 14px; }
+  `],
+})
+export class GeneralSettingsComponent {}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/models/platform-connection.model.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/models/platform-connection.model.ts
new file mode 100644
index 0000000..352b7ea
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/models/platform-connection.model.ts
@@ -0,0 +1,38 @@
+export type ConnectionStatus = 'Connected' | 'Expired' | 'NotConfigured';
+
+export interface PlatformStatus {
+  platform: string;
+  isConnected: boolean;
+  status: ConnectionStatus;
+  expiresAt: string | null;
+  lastPublishDate: string | null;
+  capabilities: PlatformCapabilities | null;
+}
+
+export interface PlatformCapabilities {
+  maxCharacters: number;
+  supportsMarkdown: boolean;
+  supportsHtml: boolean;
+  supportsImages: boolean;
+  supportsScheduling: boolean;
+  supportsThreads: boolean;
+  supportedMediaTypes: string[];
+}
+
+export interface StoreCredentialsRequest {
+  token?: string;
+  email?: string;
+  password?: string;
+}
+
+export interface ConnectionStatusResponse {
+  status: ConnectionStatus;
+  expiresAt: string | null;
+}
+
+export interface PlatformConfig {
+  platform: string;
+  displayName: string;
+  description: string;
+  connectionType: 'oauth' | 'token' | 'login' | 'none';
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/medium-token-form/medium-token-form.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/medium-token-form/medium-token-form.component.ts
new file mode 100644
index 0000000..5dc849a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/medium-token-form/medium-token-form.component.ts
@@ -0,0 +1,68 @@
+import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
+import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
+import type { StoreCredentialsRequest } from '../../models/platform-connection.model';
+
+@Component({
+  selector: 'app-medium-token-form',
+  standalone: true,
+  imports: [ReactiveFormsModule],
+  template: `
+    <form [formGroup]="form" (ngSubmit)="onSubmit()" class="token-form">
+      <p class="helper-text">
+        Find your token at Settings &gt; Security and apps &gt; Integration tokens on medium.com
+      </p>
+      <div class="field">
+        <input
+          type="password"
+          formControlName="token"
+          placeholder="Integration token"
+          class="input"
+        />
+        @if (form.controls.token.touched && form.controls.token.errors) {
+          <span class="error">
+            @if (form.controls.token.errors['required']) { Token is required }
+            @if (form.controls.token.errors['minlength']) { Token must be at least 10 characters }
+          </span>
+        }
+      </div>
+      <button type="submit" class="btn-save" [disabled]="form.invalid || loading">
+        {{ loading ? 'Saving...' : 'Save Token' }}
+      </button>
+    </form>
+  `,
+  styles: [`
+    .token-form { display: flex; flex-direction: column; gap: 12px; padding-top: 12px; }
+    .helper-text { color: #8a8a96; font-size: 12px; margin: 0; }
+    .field { display: flex; flex-direction: column; gap: 4px; }
+    .input {
+      background: #0e0e10; border: 1px solid #2c2c36; border-radius: 6px;
+      padding: 8px 12px; color: #f0f0f5; font-size: 14px; font-family: inherit;
+    }
+    .input:focus { outline: none; border-color: #c87156; }
+    .error { color: #f87171; font-size: 12px; }
+    .btn-save {
+      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
+      padding: 8px 16px; font-size: 14px; cursor: pointer; align-self: flex-start;
+      font-family: inherit;
+    }
+    .btn-save:hover:not(:disabled) { background: #d4836a; }
+    .btn-save:disabled { opacity: 0.5; cursor: not-allowed; }
+  `],
+})
+export class MediumTokenFormComponent {
+  private readonly fb = inject(FormBuilder);
+
+  @Input() loading = false;
+  @Output() submitted = new EventEmitter<StoreCredentialsRequest>();
+
+  form = this.fb.group({
+    token: ['', [Validators.required, Validators.minLength(10)]],
+  });
+
+  onSubmit(): void {
+    if (this.form.valid) {
+      this.submitted.emit({ token: this.form.value.token! });
+      this.form.reset();
+    }
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-card/platform-card.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-card/platform-card.component.spec.ts
new file mode 100644
index 0000000..fb7e0dd
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-card/platform-card.component.spec.ts
@@ -0,0 +1,159 @@
+import { ComponentFixture, TestBed } from '@angular/core/testing';
+import { PlatformCardComponent } from './platform-card.component';
+import type {
+  PlatformConfig,
+  PlatformStatus,
+} from '../../models/platform-connection.model';
+
+describe('PlatformCardComponent', () => {
+  let component: PlatformCardComponent;
+  let fixture: ComponentFixture<PlatformCardComponent>;
+
+  const oauthConfig: PlatformConfig = {
+    platform: 'LinkedIn',
+    displayName: 'LinkedIn',
+    description: 'OAuth 2.0 authentication',
+    connectionType: 'oauth',
+  };
+
+  const tokenConfig: PlatformConfig = {
+    platform: 'Medium',
+    displayName: 'Medium',
+    description: 'Integration token authentication',
+    connectionType: 'token',
+  };
+
+  const noneConfig: PlatformConfig = {
+    platform: 'Blog',
+    displayName: 'Blog',
+    description: 'matthewkruczek.ai static site',
+    connectionType: 'none',
+  };
+
+  const connectedStatus: PlatformStatus = {
+    platform: 'LinkedIn',
+    isConnected: true,
+    status: 'Connected',
+    expiresAt: '2026-08-01T00:00:00Z',
+    lastPublishDate: '2026-05-20T12:00:00Z',
+    capabilities: null,
+  };
+
+  beforeEach(async () => {
+    await TestBed.configureTestingModule({
+      imports: [PlatformCardComponent],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(PlatformCardComponent);
+    component = fixture.componentInstance;
+  });
+
+  it('should display platform name', () => {
+    component.config = oauthConfig;
+    fixture.detectChanges();
+    const el = fixture.nativeElement as HTMLElement;
+    expect(el.querySelector('.platform-name')?.textContent).toContain('LinkedIn');
+  });
+
+  it('should show Not Connected for NotConfigured status', () => {
+    component.config = oauthConfig;
+    component.status = { ...connectedStatus, status: 'NotConfigured', isConnected: false };
+    fixture.detectChanges();
+    const badge = fixture.nativeElement.querySelector('.status-badge');
+    expect(badge?.textContent).toContain('Not Connected');
+    expect(badge?.classList).toContain('status-not-configured');
+  });
+
+  it('should show Connected with green indicator', () => {
+    component.config = oauthConfig;
+    component.status = connectedStatus;
+    fixture.detectChanges();
+    const badge = fixture.nativeElement.querySelector('.status-badge');
+    expect(badge?.textContent).toContain('Connected');
+    expect(badge?.classList).toContain('status-connected');
+  });
+
+  it('should show Expired with warning indicator', () => {
+    component.config = oauthConfig;
+    component.status = { ...connectedStatus, status: 'Expired' };
+    fixture.detectChanges();
+    const badge = fixture.nativeElement.querySelector('.status-badge');
+    expect(badge?.textContent).toContain('Expired');
+    expect(badge?.classList).toContain('status-expired');
+  });
+
+  it('should show expiry date when connected', () => {
+    component.config = oauthConfig;
+    component.status = connectedStatus;
+    fixture.detectChanges();
+    const details = fixture.nativeElement.querySelector('.card-details');
+    expect(details?.textContent).toContain('Expires');
+  });
+
+  it('should show last publish date when available', () => {
+    component.config = oauthConfig;
+    component.status = connectedStatus;
+    fixture.detectChanges();
+    const details = fixture.nativeElement.querySelector('.card-details');
+    expect(details?.textContent).toContain('Last published');
+  });
+
+  it('should emit connect event for OAuth platforms', () => {
+    component.config = oauthConfig;
+    component.status = null;
+    fixture.detectChanges();
+    spyOn(component.connect, 'emit');
+
+    const btn = fixture.nativeElement.querySelector('.btn-connect') as HTMLButtonElement;
+    btn.click();
+
+    expect(component.connect.emit).toHaveBeenCalledWith('LinkedIn');
+  });
+
+  it('should emit disconnect event when Disconnect clicked', () => {
+    component.config = oauthConfig;
+    component.status = connectedStatus;
+    fixture.detectChanges();
+    spyOn(component.disconnect, 'emit');
+
+    const btn = fixture.nativeElement.querySelector('.btn-disconnect') as HTMLButtonElement;
+    btn.click();
+
+    expect(component.disconnect.emit).toHaveBeenCalledWith('LinkedIn');
+  });
+
+  it('should show Disconnect only when connected', () => {
+    component.config = oauthConfig;
+    component.status = null;
+    fixture.detectChanges();
+    expect(fixture.nativeElement.querySelector('.btn-disconnect')).toBeNull();
+  });
+
+  it('should show Connect only when not connected', () => {
+    component.config = oauthConfig;
+    component.status = connectedStatus;
+    fixture.detectChanges();
+    expect(fixture.nativeElement.querySelector('.btn-connect')).toBeNull();
+  });
+
+  it('should show Always Connected for Blog with no action buttons', () => {
+    component.config = noneConfig;
+    fixture.detectChanges();
+    const badge = fixture.nativeElement.querySelector('.status-badge');
+    expect(badge?.textContent).toContain('Always Connected');
+    expect(fixture.nativeElement.querySelector('.btn-connect')).toBeNull();
+    expect(fixture.nativeElement.querySelector('.btn-disconnect')).toBeNull();
+  });
+
+  it('should toggle token form for Medium when Connect clicked', () => {
+    component.config = tokenConfig;
+    component.status = null;
+    fixture.detectChanges();
+
+    const btn = fixture.nativeElement.querySelector('.btn-connect') as HTMLButtonElement;
+    btn.click();
+    fixture.detectChanges();
+
+    expect(fixture.nativeElement.querySelector('app-medium-token-form')).toBeTruthy();
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-card/platform-card.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-card/platform-card.component.ts
new file mode 100644
index 0000000..704f6ab
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-card/platform-card.component.ts
@@ -0,0 +1,155 @@
+import { Component, EventEmitter, Input, Output } from '@angular/core';
+import { DatePipe } from '@angular/common';
+import type {
+  PlatformConfig,
+  PlatformStatus,
+  StoreCredentialsRequest,
+} from '../../models/platform-connection.model';
+import { MediumTokenFormComponent } from '../medium-token-form/medium-token-form.component';
+import { SubstackLoginFormComponent } from '../substack-login-form/substack-login-form.component';
+
+@Component({
+  selector: 'app-platform-card',
+  standalone: true,
+  imports: [DatePipe, MediumTokenFormComponent, SubstackLoginFormComponent],
+  template: `
+    <div class="card">
+      <div class="card-header">
+        <div class="platform-info">
+          <span class="platform-name">{{ config.displayName }}</span>
+          <span class="platform-desc">{{ config.description }}</span>
+        </div>
+        <div class="status-badge" [class]="statusClass">
+          <span class="dot"></span>
+          {{ statusText }}
+        </div>
+      </div>
+
+      <div class="card-details">
+        @if (status?.expiresAt && isConnected) {
+          <span class="detail">Expires: {{ status!.expiresAt | date:'mediumDate' }}</span>
+        }
+        @if (status?.lastPublishDate) {
+          <span class="detail">Last published: {{ status!.lastPublishDate | date:'mediumDate' }}</span>
+        }
+      </div>
+
+      <div class="card-actions">
+        @if (config.connectionType === 'none') {
+          <!-- Blog: always connected, no actions -->
+        } @else if (isConnected) {
+          <button class="btn-disconnect" (click)="disconnect.emit(config.platform)" [disabled]="loading">
+            Disconnect
+          </button>
+        } @else {
+          @if (!showForm) {
+            <button class="btn-connect" (click)="onConnectClick()" [disabled]="loading">
+              {{ status?.status === 'Expired' ? 'Reconnect' : 'Connect' }}
+            </button>
+          }
+        }
+      </div>
+
+      @if (showForm && config.connectionType === 'token') {
+        <app-medium-token-form
+          [loading]="loading"
+          (submitted)="onCredentialsSubmitted($event)"
+        />
+      }
+      @if (showForm && config.connectionType === 'login') {
+        <app-substack-login-form
+          [loading]="loading"
+          [errorMessage]="credentialError"
+          (submitted)="onCredentialsSubmitted($event)"
+        />
+      }
+    </div>
+  `,
+  styles: [`
+    .card {
+      background: #141418; border: 1px solid #2c2c36; border-radius: 8px;
+      padding: 20px; display: flex; flex-direction: column; gap: 12px;
+    }
+    .card-header { display: flex; justify-content: space-between; align-items: flex-start; }
+    .platform-info { display: flex; flex-direction: column; gap: 2px; }
+    .platform-name { font-size: 16px; font-weight: 600; color: #f0f0f5; }
+    .platform-desc { font-size: 13px; color: #8a8a96; }
+    .status-badge {
+      display: flex; align-items: center; gap: 6px;
+      font-size: 13px; padding: 4px 10px; border-radius: 12px;
+    }
+    .dot { width: 8px; height: 8px; border-radius: 50%; }
+    .status-connected { color: #4ade80; background: rgba(74, 222, 128, 0.1); }
+    .status-connected .dot { background: #4ade80; }
+    .status-expired { color: #fbbf24; background: rgba(251, 191, 36, 0.1); }
+    .status-expired .dot { background: #fbbf24; }
+    .status-not-configured { color: #8a8a96; background: rgba(138, 138, 150, 0.1); }
+    .status-not-configured .dot { background: #5a5a66; }
+    .card-details { display: flex; flex-direction: column; gap: 4px; }
+    .detail { font-size: 13px; color: #8a8a96; }
+    .card-actions { display: flex; gap: 8px; }
+    .btn-connect {
+      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
+      padding: 8px 16px; font-size: 14px; cursor: pointer; font-family: inherit;
+    }
+    .btn-connect:hover:not(:disabled) { background: #d4836a; }
+    .btn-connect:disabled { opacity: 0.5; cursor: not-allowed; }
+    .btn-disconnect {
+      background: transparent; color: #f87171; border: 1px solid #f87171; border-radius: 6px;
+      padding: 8px 16px; font-size: 14px; cursor: pointer; font-family: inherit;
+    }
+    .btn-disconnect:hover:not(:disabled) { background: rgba(248, 113, 113, 0.1); }
+    .btn-disconnect:disabled { opacity: 0.5; cursor: not-allowed; }
+  `],
+})
+export class PlatformCardComponent {
+  @Input({ required: true }) config!: PlatformConfig;
+  @Input() status: PlatformStatus | null = null;
+  @Input() loading = false;
+  @Input() credentialError: string | null = null;
+  @Output() connect = new EventEmitter<string>();
+  @Output() disconnect = new EventEmitter<string>();
+  @Output() credentialsSubmitted = new EventEmitter<{
+    platform: string;
+    credentials: StoreCredentialsRequest;
+  }>();
+
+  showForm = false;
+
+  get isConnected(): boolean {
+    return this.config.connectionType === 'none' || this.status?.status === 'Connected';
+  }
+
+  get statusClass(): string {
+    if (this.config.connectionType === 'none') return 'status-connected';
+    switch (this.status?.status) {
+      case 'Connected': return 'status-connected';
+      case 'Expired': return 'status-expired';
+      default: return 'status-not-configured';
+    }
+  }
+
+  get statusText(): string {
+    if (this.config.connectionType === 'none') return 'Always Connected';
+    switch (this.status?.status) {
+      case 'Connected': return 'Connected';
+      case 'Expired': return 'Expired';
+      default: return 'Not Connected';
+    }
+  }
+
+  onConnectClick(): void {
+    if (this.config.connectionType === 'oauth') {
+      this.connect.emit(this.config.platform);
+    } else {
+      this.showForm = !this.showForm;
+    }
+  }
+
+  onCredentialsSubmitted(credentials: StoreCredentialsRequest): void {
+    this.credentialsSubmitted.emit({
+      platform: this.config.platform,
+      credentials,
+    });
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-connections.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-connections.component.spec.ts
new file mode 100644
index 0000000..94075b7
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-connections.component.spec.ts
@@ -0,0 +1,175 @@
+import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
+import { provideHttpClient } from '@angular/common/http';
+import { provideHttpClientTesting } from '@angular/common/http/testing';
+import { ActivatedRoute } from '@angular/router';
+import { of, throwError } from 'rxjs';
+import { PlatformConnectionsComponent } from './platform-connections.component';
+import { PlatformConnectionService } from '../services/platform-connection.service';
+import type { PlatformStatus } from '../models/platform-connection.model';
+
+describe('PlatformConnectionsComponent', () => {
+  let component: PlatformConnectionsComponent;
+  let fixture: ComponentFixture<PlatformConnectionsComponent>;
+  let mockService: jasmine.SpyObj<PlatformConnectionService>;
+
+  const mockPlatforms: PlatformStatus[] = [
+    {
+      platform: 'Blog',
+      isConnected: true,
+      status: 'Connected',
+      expiresAt: null,
+      lastPublishDate: null,
+      capabilities: null,
+    },
+    {
+      platform: 'LinkedIn',
+      isConnected: false,
+      status: 'NotConfigured',
+      expiresAt: null,
+      lastPublishDate: null,
+      capabilities: null,
+    },
+    {
+      platform: 'Medium',
+      isConnected: false,
+      status: 'NotConfigured',
+      expiresAt: null,
+      lastPublishDate: null,
+      capabilities: null,
+    },
+  ];
+
+  beforeEach(async () => {
+    mockService = jasmine.createSpyObj('PlatformConnectionService', [
+      'getPlatforms',
+      'getAuthorizeUrl',
+      'storeCredentials',
+      'disconnect',
+    ]);
+    mockService.getPlatforms.and.returnValue(of(mockPlatforms));
+    mockService.getAuthorizeUrl.and.callFake(
+      (p: string) => `/api/auth/${p}/authorize`
+    );
+
+    await TestBed.configureTestingModule({
+      imports: [PlatformConnectionsComponent],
+      providers: [
+        provideHttpClient(),
+        provideHttpClientTesting(),
+        { provide: PlatformConnectionService, useValue: mockService },
+        {
+          provide: ActivatedRoute,
+          useValue: { queryParams: of({}) },
+        },
+      ],
+    }).compileComponents();
+
+    fixture = TestBed.createComponent(PlatformConnectionsComponent);
+    component = fixture.componentInstance;
+  });
+
+  it('should load platforms on init', fakeAsync(() => {
+    fixture.detectChanges();
+    tick();
+    expect(mockService.getPlatforms).toHaveBeenCalled();
+    expect(component.platforms).toEqual(mockPlatforms);
+    expect(component.loading).toBeFalse();
+  }));
+
+  it('should render platform cards', fakeAsync(() => {
+    fixture.detectChanges();
+    tick();
+    fixture.detectChanges();
+    const cards = fixture.nativeElement.querySelectorAll('app-platform-card');
+    expect(cards.length).toBe(5);
+  }));
+
+  it('should show error state when API fails', fakeAsync(() => {
+    mockService.getPlatforms.and.returnValue(throwError(() => new Error('fail')));
+    fixture.detectChanges();
+    tick();
+    fixture.detectChanges();
+    expect(component.error).toBeTrue();
+    expect(fixture.nativeElement.querySelector('.error-state')).toBeTruthy();
+  }));
+
+  it('should show success notification from query param', fakeAsync(() => {
+    TestBed.resetTestingModule();
+    mockService.getPlatforms.and.returnValue(of(mockPlatforms));
+
+    TestBed.configureTestingModule({
+      imports: [PlatformConnectionsComponent],
+      providers: [
+        provideHttpClient(),
+        provideHttpClientTesting(),
+        { provide: PlatformConnectionService, useValue: mockService },
+        {
+          provide: ActivatedRoute,
+          useValue: { queryParams: of({ connected: 'LinkedIn' }) },
+        },
+      ],
+    });
+
+    const fix = TestBed.createComponent(PlatformConnectionsComponent);
+    fix.detectChanges();
+    tick();
+
+    expect(fix.componentInstance.notification?.type).toBe('success');
+    expect(fix.componentInstance.notification?.message).toContain('LinkedIn');
+  }));
+
+  it('should show error notification from query param', fakeAsync(() => {
+    TestBed.resetTestingModule();
+    mockService.getPlatforms.and.returnValue(of(mockPlatforms));
+
+    TestBed.configureTestingModule({
+      imports: [PlatformConnectionsComponent],
+      providers: [
+        provideHttpClient(),
+        provideHttpClientTesting(),
+        { provide: PlatformConnectionService, useValue: mockService },
+        {
+          provide: ActivatedRoute,
+          useValue: { queryParams: of({ error: 'auth_failed' }) },
+        },
+      ],
+    });
+
+    const fix = TestBed.createComponent(PlatformConnectionsComponent);
+    fix.detectChanges();
+    tick();
+
+    expect(fix.componentInstance.notification?.type).toBe('error');
+  }));
+
+  it('should call disconnect and reload on disconnect event', fakeAsync(() => {
+    mockService.disconnect.and.returnValue(of(void 0));
+    fixture.detectChanges();
+    tick();
+
+    const callsBefore = mockService.getPlatforms.calls.count();
+    component.onDisconnect('LinkedIn');
+    tick();
+
+    expect(mockService.disconnect).toHaveBeenCalledWith('LinkedIn');
+    expect(mockService.getPlatforms.calls.count()).toBeGreaterThan(callsBefore);
+  }));
+
+  it('should call storeCredentials and reload on submit', fakeAsync(() => {
+    mockService.storeCredentials.and.returnValue(of(void 0));
+    fixture.detectChanges();
+    tick();
+
+    const callsBefore = mockService.getPlatforms.calls.count();
+    component.onCredentialsSubmitted({
+      platform: 'Medium',
+      credentials: { token: 'test-token-12345' },
+    });
+    tick();
+
+    expect(mockService.storeCredentials).toHaveBeenCalledWith('Medium', {
+      token: 'test-token-12345',
+    });
+    expect(mockService.getPlatforms.calls.count()).toBeGreaterThan(callsBefore);
+  }));
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-connections.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-connections.component.ts
new file mode 100644
index 0000000..2310350
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/platform-connections.component.ts
@@ -0,0 +1,168 @@
+import { Component, OnInit, inject } from '@angular/core';
+import { ActivatedRoute } from '@angular/router';
+import { PlatformConnectionService } from '../services/platform-connection.service';
+import { PlatformCardComponent } from './platform-card/platform-card.component';
+import type {
+  PlatformConfig,
+  PlatformStatus,
+  StoreCredentialsRequest,
+} from '../models/platform-connection.model';
+
+@Component({
+  selector: 'app-platform-connections',
+  standalone: true,
+  imports: [PlatformCardComponent],
+  template: `
+    @if (notification) {
+      <div class="notification" [class]="notification.type">
+        {{ notification.message }}
+        <button class="dismiss" (click)="notification = null">&times;</button>
+      </div>
+    }
+
+    @if (loading) {
+      <div class="loading">Loading platforms...</div>
+    } @else if (error) {
+      <div class="error-state">
+        <p>Failed to load platforms.</p>
+        <button class="btn-retry" (click)="loadPlatforms()">Retry</button>
+      </div>
+    } @else {
+      <div class="grid">
+        @for (config of platformConfigs; track config.platform) {
+          <app-platform-card
+            [config]="config"
+            [status]="getStatus(config.platform)"
+            [loading]="loadingPlatform === config.platform"
+            [credentialError]="credentialErrors[config.platform] ?? null"
+            (connect)="onConnect($event)"
+            (disconnect)="onDisconnect($event)"
+            (credentialsSubmitted)="onCredentialsSubmitted($event)"
+          />
+        }
+      </div>
+    }
+  `,
+  styles: [`
+    .grid {
+      display: grid; grid-template-columns: repeat(2, 1fr); gap: 16px;
+    }
+    @media (max-width: 768px) {
+      .grid { grid-template-columns: 1fr; }
+    }
+    .loading { color: #8a8a96; padding: 40px 0; text-align: center; }
+    .error-state { color: #8a8a96; padding: 40px 0; text-align: center; }
+    .btn-retry {
+      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
+      padding: 8px 16px; font-size: 14px; cursor: pointer; margin-top: 12px;
+      font-family: inherit;
+    }
+    .notification {
+      display: flex; align-items: center; justify-content: space-between;
+      padding: 12px 16px; border-radius: 8px; margin-bottom: 16px; font-size: 14px;
+    }
+    .notification.success { background: rgba(74, 222, 128, 0.1); color: #4ade80; }
+    .notification.error { background: rgba(248, 113, 113, 0.1); color: #f87171; }
+    .dismiss {
+      background: none; border: none; color: inherit; cursor: pointer;
+      font-size: 18px; line-height: 1;
+    }
+  `],
+})
+export class PlatformConnectionsComponent implements OnInit {
+  private readonly service = inject(PlatformConnectionService);
+  private readonly route = inject(ActivatedRoute);
+
+  readonly platformConfigs: PlatformConfig[] = [
+    { platform: 'Blog', displayName: 'Blog', description: 'matthewkruczek.ai static site', connectionType: 'none' },
+    { platform: 'Medium', displayName: 'Medium', description: 'Integration token authentication', connectionType: 'token' },
+    { platform: 'Substack', displayName: 'Substack', description: 'Email/password authentication', connectionType: 'login' },
+    { platform: 'LinkedIn', displayName: 'LinkedIn', description: 'OAuth 2.0 authentication', connectionType: 'oauth' },
+    { platform: 'Twitter', displayName: 'Twitter / X', description: 'OAuth 2.0 with PKCE', connectionType: 'oauth' },
+  ];
+
+  platforms: PlatformStatus[] = [];
+  loading = true;
+  error = false;
+  loadingPlatform: string | null = null;
+  credentialErrors: Record<string, string> = {};
+  notification: { type: 'success' | 'error'; message: string } | null = null;
+
+  ngOnInit(): void {
+    this.loadPlatforms();
+    this.route.queryParams.subscribe((params) => {
+      if (params['connected']) {
+        this.notification = {
+          type: 'success',
+          message: `${params['connected']} connected successfully`,
+        };
+        this.loadPlatforms();
+      }
+      if (params['error']) {
+        this.notification = {
+          type: 'error',
+          message: 'Authentication failed. Please try again.',
+        };
+      }
+    });
+  }
+
+  loadPlatforms(): void {
+    this.loading = true;
+    this.error = false;
+    this.service.getPlatforms().subscribe({
+      next: (platforms) => {
+        this.platforms = platforms;
+        this.loading = false;
+      },
+      error: () => {
+        this.error = true;
+        this.loading = false;
+      },
+    });
+  }
+
+  getStatus(platform: string): PlatformStatus | null {
+    return this.platforms.find((p) => p.platform === platform) ?? null;
+  }
+
+  onConnect(platform: string): void {
+    window.location.href = this.service.getAuthorizeUrl(platform);
+  }
+
+  onDisconnect(platform: string): void {
+    this.loadingPlatform = platform;
+    this.service.disconnect(platform).subscribe({
+      next: () => {
+        this.loadingPlatform = null;
+        this.loadPlatforms();
+      },
+      error: () => {
+        this.loadingPlatform = null;
+      },
+    });
+  }
+
+  onCredentialsSubmitted(event: {
+    platform: string;
+    credentials: StoreCredentialsRequest;
+  }): void {
+    this.loadingPlatform = event.platform;
+    delete this.credentialErrors[event.platform];
+    this.service.storeCredentials(event.platform, event.credentials).subscribe({
+      next: () => {
+        this.loadingPlatform = null;
+        this.notification = {
+          type: 'success',
+          message: `${event.platform} credentials saved`,
+        };
+        this.loadPlatforms();
+      },
+      error: (err) => {
+        this.loadingPlatform = null;
+        this.credentialErrors[event.platform] =
+          err.error?.message ?? 'Failed to save credentials';
+      },
+    });
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/substack-login-form/substack-login-form.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/substack-login-form/substack-login-form.component.ts
new file mode 100644
index 0000000..5e42bcb
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/platforms/substack-login-form/substack-login-form.component.ts
@@ -0,0 +1,87 @@
+import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
+import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
+import type { StoreCredentialsRequest } from '../../models/platform-connection.model';
+
+@Component({
+  selector: 'app-substack-login-form',
+  standalone: true,
+  imports: [ReactiveFormsModule],
+  template: `
+    <form [formGroup]="form" (ngSubmit)="onSubmit()" class="login-form">
+      <p class="warning-text">
+        Your password is used once to authenticate and is not stored. Only encrypted session cookies are saved.
+      </p>
+      <div class="field">
+        <input
+          type="email"
+          formControlName="email"
+          placeholder="Email"
+          class="input"
+        />
+        @if (form.controls.email.touched && form.controls.email.errors) {
+          <span class="error">
+            @if (form.controls.email.errors['required']) { Email is required }
+            @if (form.controls.email.errors['email']) { Enter a valid email }
+          </span>
+        }
+      </div>
+      <div class="field">
+        <input
+          type="password"
+          formControlName="password"
+          placeholder="Password"
+          class="input"
+        />
+        @if (form.controls.password.touched && form.controls.password.errors) {
+          <span class="error">Password is required</span>
+        }
+      </div>
+      @if (errorMessage) {
+        <span class="error">{{ errorMessage }}</span>
+      }
+      <button type="submit" class="btn-login" [disabled]="form.invalid || loading">
+        {{ loading ? 'Logging in...' : 'Login' }}
+      </button>
+    </form>
+  `,
+  styles: [`
+    .login-form { display: flex; flex-direction: column; gap: 12px; padding-top: 12px; }
+    .warning-text { color: #8a8a96; font-size: 12px; margin: 0; font-style: italic; }
+    .field { display: flex; flex-direction: column; gap: 4px; }
+    .input {
+      background: #0e0e10; border: 1px solid #2c2c36; border-radius: 6px;
+      padding: 8px 12px; color: #f0f0f5; font-size: 14px; font-family: inherit;
+    }
+    .input:focus { outline: none; border-color: #c87156; }
+    .error { color: #f87171; font-size: 12px; }
+    .btn-login {
+      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
+      padding: 8px 16px; font-size: 14px; cursor: pointer; align-self: flex-start;
+      font-family: inherit;
+    }
+    .btn-login:hover:not(:disabled) { background: #d4836a; }
+    .btn-login:disabled { opacity: 0.5; cursor: not-allowed; }
+  `],
+})
+export class SubstackLoginFormComponent {
+  private readonly fb = inject(FormBuilder);
+
+  @Input() loading = false;
+  @Input() errorMessage: string | null = null;
+  @Output() submitted = new EventEmitter<StoreCredentialsRequest>();
+
+  form = this.fb.group({
+    email: ['', [Validators.required, Validators.email]],
+    password: ['', Validators.required],
+  });
+
+  onSubmit(): void {
+    if (this.form.valid) {
+      this.submitted.emit({
+        email: this.form.value.email!,
+        password: this.form.value.password!,
+      });
+      this.form.controls.password.reset();
+    }
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/services/platform-connection.service.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/services/platform-connection.service.spec.ts
new file mode 100644
index 0000000..4ef7377
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/services/platform-connection.service.spec.ts
@@ -0,0 +1,125 @@
+import { TestBed } from '@angular/core/testing';
+import {
+  HttpTestingController,
+  provideHttpClientTesting,
+} from '@angular/common/http/testing';
+import { provideHttpClient } from '@angular/common/http';
+import { PlatformConnectionService } from './platform-connection.service';
+import type {
+  PlatformStatus,
+  ConnectionStatusResponse,
+} from '../models/platform-connection.model';
+
+describe('PlatformConnectionService', () => {
+  let service: PlatformConnectionService;
+  let httpMock: HttpTestingController;
+
+  beforeEach(() => {
+    TestBed.configureTestingModule({
+      providers: [
+        PlatformConnectionService,
+        provideHttpClient(),
+        provideHttpClientTesting(),
+      ],
+    });
+    service = TestBed.inject(PlatformConnectionService);
+    httpMock = TestBed.inject(HttpTestingController);
+  });
+
+  afterEach(() => {
+    httpMock.verify();
+  });
+
+  it('getPlatforms() sends GET /api/platforms', () => {
+    const mockPlatforms: PlatformStatus[] = [
+      {
+        platform: 'LinkedIn',
+        isConnected: true,
+        status: 'Connected',
+        expiresAt: '2026-08-01T00:00:00Z',
+        lastPublishDate: null,
+        capabilities: null,
+      },
+      {
+        platform: 'Medium',
+        isConnected: false,
+        status: 'NotConfigured',
+        expiresAt: null,
+        lastPublishDate: null,
+        capabilities: null,
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
+
+  it('getStatus() sends GET /api/auth/{platform}/status', () => {
+    const mockStatus: ConnectionStatusResponse = {
+      status: 'Connected',
+      expiresAt: '2026-08-01T00:00:00Z',
+    };
+
+    service.getStatus('LinkedIn').subscribe((result) => {
+      expect(result).toEqual(mockStatus);
+    });
+
+    const req = httpMock.expectOne('/api/auth/LinkedIn/status');
+    expect(req.request.method).toBe('GET');
+    req.flush(mockStatus);
+  });
+
+  it('getAuthorizeUrl() returns correct URL without HTTP call', () => {
+    const url = service.getAuthorizeUrl('LinkedIn');
+    expect(url).toBe('/api/auth/LinkedIn/authorize');
+  });
+
+  it('storeCredentials() sends POST for Medium token', () => {
+    service
+      .storeCredentials('Medium', { token: 'abc123' })
+      .subscribe();
+
+    const req = httpMock.expectOne('/api/platforms/Medium/credentials');
+    expect(req.request.method).toBe('POST');
+    expect(req.request.body).toEqual({ token: 'abc123' });
+    req.flush(null);
+  });
+
+  it('storeCredentials() sends POST for Substack login', () => {
+    service
+      .storeCredentials('Substack', {
+        email: 'a@b.com',
+        password: 'pw',
+      })
+      .subscribe();
+
+    const req = httpMock.expectOne('/api/platforms/Substack/credentials');
+    expect(req.request.method).toBe('POST');
+    expect(req.request.body).toEqual({ email: 'a@b.com', password: 'pw' });
+    req.flush(null);
+  });
+
+  it('disconnect() sends DELETE /api/auth/{platform}', () => {
+    service.disconnect('LinkedIn').subscribe();
+
+    const req = httpMock.expectOne('/api/auth/LinkedIn');
+    expect(req.request.method).toBe('DELETE');
+    req.flush(null);
+  });
+
+  it('propagates HTTP errors', () => {
+    service.getStatus('LinkedIn').subscribe({
+      error: (err) => {
+        expect(err.status).toBe(500);
+      },
+    });
+
+    const req = httpMock.expectOne('/api/auth/LinkedIn/status');
+    req.flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
+  });
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/services/platform-connection.service.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/services/platform-connection.service.ts
new file mode 100644
index 0000000..f87978d
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/services/platform-connection.service.ts
@@ -0,0 +1,43 @@
+import { Injectable } from '@angular/core';
+import { HttpClient } from '@angular/common/http';
+import { Observable } from 'rxjs';
+import {
+  PlatformStatus,
+  ConnectionStatusResponse,
+  StoreCredentialsRequest,
+} from '../models/platform-connection.model';
+
+@Injectable({ providedIn: 'root' })
+export class PlatformConnectionService {
+  private readonly baseUrl = '/api';
+
+  constructor(private readonly http: HttpClient) {}
+
+  getPlatforms(): Observable<PlatformStatus[]> {
+    return this.http.get<PlatformStatus[]>(`${this.baseUrl}/platforms`);
+  }
+
+  getStatus(platform: string): Observable<ConnectionStatusResponse> {
+    return this.http.get<ConnectionStatusResponse>(
+      `${this.baseUrl}/auth/${platform}/status`
+    );
+  }
+
+  getAuthorizeUrl(platform: string): string {
+    return `${this.baseUrl}/auth/${platform}/authorize`;
+  }
+
+  storeCredentials(
+    platform: string,
+    request: StoreCredentialsRequest
+  ): Observable<void> {
+    return this.http.post<void>(
+      `${this.baseUrl}/platforms/${platform}/credentials`,
+      request
+    );
+  }
+
+  disconnect(platform: string): Observable<void> {
+    return this.http.delete<void>(`${this.baseUrl}/auth/${platform}`);
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.component.ts
index 0d94b0f..8d46ab1 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.component.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.component.ts
@@ -1,18 +1,35 @@
 import { Component } from '@angular/core';
+import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
 
 @Component({
   selector: 'app-settings',
   standalone: true,
+  imports: [RouterLink, RouterLinkActive, RouterOutlet],
   template: `
     <div class="page">
       <h1>Settings</h1>
-      <p class="subtitle">Configuration</p>
+      <nav class="tabs">
+        <a routerLink="general" routerLinkActive="active">General</a>
+        <a routerLink="platforms" routerLinkActive="active">Platforms</a>
+      </nav>
+      <div class="tab-content">
+        <router-outlet />
+      </div>
     </div>
   `,
   styles: [`
     .page { padding: 8px 0; }
-    h1 { font-size: 24px; font-weight: 600; margin: 0 0 4px; color: #f0f6fc; }
-    .subtitle { color: #8b949e; margin: 0; font-size: 14px; }
-  `]
+    h1 { font-size: 24px; font-weight: 600; margin: 0 0 16px; color: #f0f0f5; }
+    .tabs {
+      display: flex; gap: 0; border-bottom: 1px solid #2c2c36; margin-bottom: 24px;
+    }
+    .tabs a {
+      padding: 10px 20px; color: #8a8a96; text-decoration: none; font-size: 14px;
+      border-bottom: 2px solid transparent; transition: color 0.15s, border-color 0.15s;
+    }
+    .tabs a:hover { color: #f0f0f5; }
+    .tabs a.active { color: #c87156; border-bottom-color: #c87156; }
+    .tab-content { }
+  `],
 })
 export class SettingsComponent {}
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.routes.ts b/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.routes.ts
new file mode 100644
index 0000000..2738ecf
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/settings/settings.routes.ts
@@ -0,0 +1,26 @@
+import { Routes } from '@angular/router';
+import { SettingsComponent } from './settings.component';
+
+export const SETTINGS_ROUTES: Routes = [
+  {
+    path: '',
+    component: SettingsComponent,
+    children: [
+      { path: '', redirectTo: 'general', pathMatch: 'full' },
+      {
+        path: 'general',
+        loadComponent: () =>
+          import('./general/general-settings.component').then(
+            (m) => m.GeneralSettingsComponent
+          ),
+      },
+      {
+        path: 'platforms',
+        loadComponent: () =>
+          import('./platforms/platform-connections.component').then(
+            (m) => m.PlatformConnectionsComponent
+          ),
+      },
+    ],
+  },
+];
