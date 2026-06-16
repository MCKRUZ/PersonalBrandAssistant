diff --git a/src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.spec.ts b/src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.spec.ts
index 5da1870..ea93ea6 100644
--- a/src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.spec.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.spec.ts
@@ -224,6 +224,38 @@ describe('IdeaService', () => {
     req.flush(5);
   });
 
+  it('getBrandProfile() sends GET /api/brand-ranking-profile', () => {
+    const profile = {
+      id: 'p', version: 2, positioning: 'Pos', audiencePrimary: 'Aud', audienceSecondary: null,
+      halfLifeDays: 7, decayFloor: 0.075, antiTopicMultiplier: 0.1, authorityBoost: 1.2,
+      pillars: [], authorityTopics: [], antiTopics: [], voiceMarkers: [],
+      concurrencyToken: '0', updatedAt: '2026-01-01T00:00:00Z',
+    };
+
+    service.getBrandProfile().subscribe((result) => expect(result).toEqual(profile));
+
+    const req = httpMock.expectOne('/api/brand-ranking-profile');
+    expect(req.request.method).toBe('GET');
+    req.flush(profile);
+  });
+
+  it('updateBrandProfile() sends PUT /api/brand-ranking-profile with the body incl. concurrency token', () => {
+    const body = {
+      positioning: 'Pos', audiencePrimary: 'Aud', audienceSecondary: null,
+      halfLifeDays: 7, decayFloor: 0.075, antiTopicMultiplier: 0.1, authorityBoost: 1.2,
+      pillars: [{ id: 'p1', name: 'P1', description: 'D1', weight: 0.6, order: 0 }],
+      authorityTopics: ['a'], antiTopics: ['x'], voiceMarkers: ['v'], concurrencyToken: '42',
+    };
+
+    service.updateBrandProfile(body).subscribe();
+
+    const req = httpMock.expectOne('/api/brand-ranking-profile');
+    expect(req.request.method).toBe('PUT');
+    expect(req.request.body).toEqual(body);
+    expect(req.request.body.concurrencyToken).toBe('42');
+    req.flush({ ...body, id: 'p', version: 3, updatedAt: '2026-01-01T00:00:00Z' });
+  });
+
   it('propagates HTTP errors', () => {
     service.getById('bad-id').subscribe({
       error: (err) => {
diff --git a/src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.ts b/src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.ts
index d25dd5e..f6fde44 100644
--- a/src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/core/services/idea.service.ts
@@ -12,13 +12,26 @@ import {
   IdeaSortState,
 } from '../../models/idea.model';
 import { PagedResult } from '../../models/pagination.model';
+import {
+  BrandRankingProfile,
+  UpdateBrandProfileRequest,
+} from '../../models/brand-profile.model';
 
 @Injectable({ providedIn: 'root' })
 export class IdeaService {
   private readonly baseUrl = '/api';
+  private readonly brandProfileUrl = `${this.baseUrl}/brand-ranking-profile`;
 
   constructor(private readonly http: HttpClient) {}
 
+  getBrandProfile(): Observable<BrandRankingProfile> {
+    return this.http.get<BrandRankingProfile>(this.brandProfileUrl);
+  }
+
+  updateBrandProfile(request: UpdateBrandProfileRequest): Observable<BrandRankingProfile> {
+    return this.http.put<BrandRankingProfile>(this.brandProfileUrl, request);
+  }
+
   list(
     filter: Partial<IdeaFilterState>,
     page: number,
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.routes.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.routes.ts
index 3b8939d..c0e6411 100644
--- a/src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.routes.ts
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/ideas.routes.ts
@@ -13,4 +13,11 @@ export const IDEAS_ROUTES: Routes = [
         (m) => m.IdeaSourcesPageComponent
       ),
   },
+  {
+    path: 'brand-profile',
+    loadComponent: () =>
+      import('./pages/brand-profile/brand-profile.component').then(
+        (m) => m.BrandProfileComponent
+      ),
+  },
 ];
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/pages/brand-profile/brand-profile.component.spec.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/pages/brand-profile/brand-profile.component.spec.ts
new file mode 100644
index 0000000..12d0ad2
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/pages/brand-profile/brand-profile.component.spec.ts
@@ -0,0 +1,122 @@
+import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
+import { of, throwError } from 'rxjs';
+import { ConfirmationService, Confirmation } from 'primeng/api';
+import { provideNoopAnimations } from '@angular/platform-browser/animations';
+import { BrandProfileComponent } from './brand-profile.component';
+import { IdeaService } from '../../../../core/services/idea.service';
+import { IdeaStore } from '../../store/idea.store';
+import { BrandRankingProfile } from '../../../../models/brand-profile.model';
+
+function makeProfile(): BrandRankingProfile {
+  return {
+    id: 'profile-1', version: 1,
+    positioning: 'Pos', audiencePrimary: 'Aud', audienceSecondary: null,
+    halfLifeDays: 7, decayFloor: 0.075, antiTopicMultiplier: 0.1, authorityBoost: 1.2,
+    pillars: [{ id: 'p1', name: 'P1', description: 'D1', weight: 0.6, order: 0 }],
+    authorityTopics: ['a1'], antiTopics: ['x1'], voiceMarkers: ['v1'],
+    concurrencyToken: '0', updatedAt: '2026-01-01T00:00:00Z',
+  };
+}
+
+describe('BrandProfileComponent', () => {
+  let fixture: ComponentFixture<BrandProfileComponent>;
+  let component: BrandProfileComponent;
+  let ideaService: jasmine.SpyObj<IdeaService>;
+  let store: { loadIdeas: jasmine.Spy };
+  let confirmSpy: jasmine.Spy;
+
+  beforeEach(() => {
+    ideaService = jasmine.createSpyObj<IdeaService>('IdeaService', [
+      'getBrandProfile', 'updateBrandProfile',
+    ]);
+    ideaService.getBrandProfile.and.returnValue(of(makeProfile()));
+    ideaService.updateBrandProfile.and.returnValue(of(makeProfile()));
+    store = { loadIdeas: jasmine.createSpy('loadIdeas') };
+
+    TestBed.configureTestingModule({
+      imports: [BrandProfileComponent],
+      providers: [
+        provideNoopAnimations(),
+        { provide: IdeaService, useValue: ideaService },
+        { provide: IdeaStore, useValue: store },
+      ],
+    });
+
+    fixture = TestBed.createComponent(BrandProfileComponent);
+    component = fixture.componentInstance;
+    // Spy on the component-scoped ConfirmationService instance.
+    confirmSpy = spyOn(
+      fixture.debugElement.injector.get(ConfirmationService),
+      'confirm'
+    ).and.callThrough();
+    component.ngOnInit(); // getBrandProfile is synchronous (of) -> form populated, ready
+  });
+
+  it('loads the active profile and populates the form', () => {
+    expect(ideaService.getBrandProfile).toHaveBeenCalled();
+    expect(component.form.get('positioning')!.value).toBe('Pos');
+    expect(component.pillars.length).toBe(1);
+    expect(component.version()).toBe(1);
+  });
+
+  it('weight change auto-applies a weights-only update and reloads ideas, with NO confirm dialog', fakeAsync(() => {
+    component.pillars.at(0).get('weight')!.setValue(0.9);
+    tick(400);
+
+    expect(ideaService.updateBrandProfile).toHaveBeenCalledTimes(1);
+    const req = ideaService.updateBrandProfile.calls.mostRecent().args[0];
+    expect(req.positioning).toBe('Pos');        // baseline definition, not smuggled
+    expect(req.pillars[0].weight).toBe(0.9);     // the new weight
+    expect(req.concurrencyToken).toBe('0');
+    expect(store.loadIdeas).toHaveBeenCalled();
+    expect(confirmSpy).not.toHaveBeenCalled();
+  }));
+
+  it('stages a pillar-definition edit — no immediate PUT, requires the confirm dialog', fakeAsync(() => {
+    component.pillars.at(0).get('name')!.setValue('Renamed Pillar');
+    tick(400);
+
+    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled();
+    expect(component.definitionsDirty()).toBeTrue();
+  }));
+
+  it('the confirm dialog warns about LLM cost, and accepting sends the definition update', fakeAsync(() => {
+    component.pillars.at(0).get('name')!.setValue('Renamed Pillar');
+    tick(400);
+
+    component.saveDefinitions();
+
+    expect(confirmSpy).toHaveBeenCalled();
+    const cfg = confirmSpy.calls.mostRecent().args[0] as Confirmation;
+    expect(cfg.message!.toLowerCase()).toContain('token'); // warns about LLM token cost
+    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled(); // not until accept
+
+    cfg.accept!();
+    expect(ideaService.updateBrandProfile).toHaveBeenCalledTimes(1);
+    const req = ideaService.updateBrandProfile.calls.mostRecent().args[0];
+    expect(req.pillars[0].name).toBe('Renamed Pillar');
+  }));
+
+  it('blocks save when validation fails (weight out of range)', () => {
+    component.pillars.at(0).get('weight')!.setValue(1.5); // invalid
+    component.saveDefinitions();
+    expect(confirmSpy).not.toHaveBeenCalled();
+  });
+
+  it('blocks save when positioning is empty', () => {
+    component.form.get('positioning')!.setValue('');
+    component.saveDefinitions();
+    expect(confirmSpy).not.toHaveBeenCalled();
+  });
+
+  it('surfaces a 409 conflict on save instead of losing the edit', fakeAsync(() => {
+    ideaService.updateBrandProfile.and.returnValue(throwError(() => ({ status: 409 })));
+    component.pillars.at(0).get('name')!.setValue('Renamed Pillar');
+    tick(400);
+
+    component.saveDefinitions();
+    (confirmSpy.calls.mostRecent().args[0] as Confirmation).accept!();
+
+    expect(component.conflictMessage()).toContain('changed elsewhere');
+  }));
+});
diff --git a/src/PersonalBrandAssistant.Web/src/app/features/ideas/pages/brand-profile/brand-profile.component.ts b/src/PersonalBrandAssistant.Web/src/app/features/ideas/pages/brand-profile/brand-profile.component.ts
new file mode 100644
index 0000000..a03678b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/features/ideas/pages/brand-profile/brand-profile.component.ts
@@ -0,0 +1,368 @@
+import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
+import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
+import {
+  AbstractControl,
+  FormArray,
+  FormBuilder,
+  FormGroup,
+  ReactiveFormsModule,
+  Validators,
+} from '@angular/forms';
+import { debounceTime } from 'rxjs';
+import { ConfirmationService } from 'primeng/api';
+import { ConfirmDialogModule } from 'primeng/confirmdialog';
+import { SliderModule } from 'primeng/slider';
+import { InputTextModule } from 'primeng/inputtext';
+import { TextareaModule } from 'primeng/textarea';
+import { InputNumberModule } from 'primeng/inputnumber';
+import { ButtonModule } from 'primeng/button';
+import { IdeaService } from '../../../../core/services/idea.service';
+import { IdeaStore } from '../../store/idea.store';
+import {
+  BrandPillar,
+  BrandRankingProfile,
+  UpdateBrandProfileRequest,
+} from '../../../../models/brand-profile.model';
+
+const positive = (c: AbstractControl) => (c.value > 0 ? null : { positive: true });
+
+const splitLines = (s: string | null | undefined): string[] =>
+  (s ?? '')
+    .split('\n')
+    .map((t) => t.trim())
+    .filter((t) => t.length > 0);
+
+const arraysEqual = (a: string[], b: string[]): boolean =>
+  a.length === b.length && a.every((v, i) => v === b[i]);
+
+@Component({
+  selector: 'app-brand-profile',
+  standalone: true,
+  imports: [
+    ReactiveFormsModule,
+    ConfirmDialogModule,
+    SliderModule,
+    InputTextModule,
+    TextareaModule,
+    InputNumberModule,
+    ButtonModule,
+  ],
+  providers: [ConfirmationService],
+  template: `
+    <div class="brand-profile">
+      <h2>Brand Ranking Profile <small>v{{ version() }}</small></h2>
+
+      @if (conflictMessage(); as msg) {
+        <div class="conflict" data-testid="conflict-message">{{ msg }}</div>
+      }
+      @if (saveNotice(); as note) {
+        <div class="notice" data-testid="save-notice">{{ note }}</div>
+      }
+
+      @if (!loading()) {
+        <form [formGroup]="form">
+          <section>
+            <h3>Positioning &amp; audience</h3>
+            <label>Positioning
+              <input type="text" pInputText formControlName="positioning" data-testid="positioning" />
+            </label>
+            <label>Primary audience
+              <input type="text" pInputText formControlName="audiencePrimary" data-testid="audience-primary" />
+            </label>
+            <label>Secondary audience
+              <input type="text" pInputText formControlName="audienceSecondary" />
+            </label>
+          </section>
+
+          <section>
+            <h3>Ranking knobs (auto-applied)</h3>
+            <label>Half-life (days)
+              <p-inputNumber formControlName="halfLifeDays" [minFractionDigits]="0" [maxFractionDigits]="2" />
+            </label>
+            <label>Decay floor
+              <p-inputNumber formControlName="decayFloor" [minFractionDigits]="0" [maxFractionDigits]="3" />
+            </label>
+            <label>Anti-topic multiplier
+              <p-inputNumber formControlName="antiTopicMultiplier" [minFractionDigits]="0" [maxFractionDigits]="3" />
+            </label>
+            <label>Authority boost
+              <p-inputNumber formControlName="authorityBoost" [minFractionDigits]="0" [maxFractionDigits]="3" />
+            </label>
+          </section>
+
+          <section formArrayName="pillars">
+            <h3>Pillars</h3>
+            @for (pillar of pillars.controls; track pillar; let i = $index) {
+              <div class="pillar" [formGroupName]="i">
+                <input type="text" pInputText formControlName="name"
+                       [attr.data-testid]="'pillar-name-' + i" placeholder="Name" />
+                <textarea pTextarea formControlName="description"
+                          [attr.data-testid]="'pillar-desc-' + i" placeholder="Description"></textarea>
+                <p-slider formControlName="weight" [min]="0" [max]="1" [step]="0.05"
+                          [attr.data-testid]="'pillar-weight-' + i" />
+                <button type="button" pButton severity="secondary" (click)="removePillar(i)"
+                        [attr.data-testid]="'pillar-remove-' + i">Remove</button>
+              </div>
+            }
+            <button type="button" pButton (click)="addPillar()" data-testid="pillar-add">Add pillar</button>
+          </section>
+
+          <section>
+            <h3>Topics &amp; voice (one per line — definition changes)</h3>
+            <label>Authority topics
+              <textarea pTextarea formControlName="authorityTopics" data-testid="authority-topics"></textarea>
+            </label>
+            <label>Anti topics
+              <textarea pTextarea formControlName="antiTopics" data-testid="anti-topics"></textarea>
+            </label>
+            <label>Voice markers
+              <textarea pTextarea formControlName="voiceMarkers" data-testid="voice-markers"></textarea>
+            </label>
+          </section>
+
+          <button type="button" pButton (click)="saveDefinitions()"
+                  [disabled]="form.invalid || !definitionsDirty()" data-testid="save-rescore">
+            Save &amp; re-score
+          </button>
+        </form>
+      }
+
+      <p-confirmDialog />
+    </div>
+  `,
+})
+export class BrandProfileComponent implements OnInit {
+  private readonly fb = inject(FormBuilder);
+  private readonly ideaService = inject(IdeaService);
+  private readonly ideaStore = inject(IdeaStore);
+  private readonly confirmationService = inject(ConfirmationService);
+  private readonly destroyRef = inject(DestroyRef);
+
+  readonly version = signal(0);
+  readonly loading = signal(true);
+  readonly definitionsDirty = signal(false);
+  readonly conflictMessage = signal<string | null>(null);
+  readonly saveNotice = signal<string | null>(null);
+
+  private baseline: BrandRankingProfile | null = null;
+  private token = '';
+  private ready = false; // suppresses auto-apply while we patch the form programmatically
+
+  readonly form = this.fb.group({
+    positioning: ['', Validators.required],
+    audiencePrimary: ['', Validators.required],
+    audienceSecondary: [''],
+    halfLifeDays: [7, [Validators.required, positive]],
+    decayFloor: [0.075, [Validators.required, Validators.min(0), Validators.max(1)]],
+    antiTopicMultiplier: [0.1, [Validators.required, positive]],
+    authorityBoost: [1.2, [Validators.required, positive]],
+    authorityTopics: [''],
+    antiTopics: [''],
+    voiceMarkers: [''],
+    pillars: this.fb.array<FormGroup>([]),
+  });
+
+  get pillars(): FormArray {
+    return this.form.get('pillars') as FormArray;
+  }
+
+  ngOnInit(): void {
+    this.ideaService.getBrandProfile().subscribe({
+      next: (profile) => this.applyServerState(profile, true),
+      error: () => {
+        this.loading.set(false);
+        this.conflictMessage.set('Failed to load the brand profile.');
+      },
+    });
+
+    this.form.valueChanges
+      .pipe(debounceTime(400), takeUntilDestroyed(this.destroyRef))
+      .subscribe(() => {
+        if (!this.ready || !this.baseline) return;
+        this.definitionsDirty.set(this.definitionsDiffer());
+        // Weight/knob edits auto-apply as a weights-only PUT (no version bump, no re-score). The request
+        // carries BASELINE definitions, so a STAGED definition edit can never ride along — it still needs
+        // the explicit "Save & re-score" confirm.
+        if (this.form.valid && this.weightsKnobsDiffer()) {
+          this.applyWeightsOnly();
+        }
+      });
+  }
+
+  addPillar(): void {
+    this.pillars.push(this.buildPillarGroup({
+      id: crypto.randomUUID(), name: '', description: '', weight: 0, order: this.pillars.length,
+    }));
+  }
+
+  removePillar(index: number): void {
+    this.pillars.removeAt(index);
+  }
+
+  saveDefinitions(): void {
+    if (this.form.invalid) return;
+    this.confirmationService.confirm({
+      header: 'Save & re-score',
+      message:
+        'Re-scoring all recent ideas against the updated profile will spend LLM tokens. Continue?',
+      accept: () => {
+        this.conflictMessage.set(null);
+        this.ideaService.updateBrandProfile(this.buildDefinitionRequest()).subscribe({
+          next: (profile) => {
+            this.applyServerState(profile, true);
+            this.saveNotice.set('Re-score queued.');
+          },
+          error: (err) => this.handleError(err),
+        });
+      },
+    });
+  }
+
+  private applyWeightsOnly(): void {
+    this.conflictMessage.set(null);
+    this.ideaService.updateBrandProfile(this.buildWeightsOnlyRequest()).subscribe({
+      next: (profile) => {
+        this.applyServerState(profile, false); // refresh baseline/token; keep the user's in-flight edits
+        this.ideaStore.loadIdeas();
+      },
+      error: (err) => this.handleError(err),
+    });
+  }
+
+  private buildWeightsOnlyRequest(): UpdateBrandProfileRequest {
+    const b = this.baseline!;
+    const v = this.form.getRawValue();
+    const formById = new Map(
+      (v.pillars as BrandPillar[]).map((p) => [p.id, p])
+    );
+    return {
+      positioning: b.positioning,
+      audiencePrimary: b.audiencePrimary,
+      audienceSecondary: b.audienceSecondary,
+      halfLifeDays: v.halfLifeDays!,
+      decayFloor: v.decayFloor!,
+      antiTopicMultiplier: v.antiTopicMultiplier!,
+      authorityBoost: v.authorityBoost!,
+      // Baseline pillar set + names/descriptions, with weight/order from the form (no adds/removes).
+      pillars: b.pillars.map((bp) => {
+        const fp = formById.get(bp.id);
+        return {
+          id: bp.id, name: bp.name, description: bp.description,
+          weight: fp ? fp.weight : bp.weight, order: fp ? fp.order : bp.order,
+        };
+      }),
+      authorityTopics: b.authorityTopics,
+      antiTopics: b.antiTopics,
+      voiceMarkers: b.voiceMarkers,
+      concurrencyToken: this.token,
+    };
+  }
+
+  private buildDefinitionRequest(): UpdateBrandProfileRequest {
+    const v = this.form.getRawValue();
+    return {
+      positioning: v.positioning!,
+      audiencePrimary: v.audiencePrimary!,
+      audienceSecondary: v.audienceSecondary || null,
+      halfLifeDays: v.halfLifeDays!,
+      decayFloor: v.decayFloor!,
+      antiTopicMultiplier: v.antiTopicMultiplier!,
+      authorityBoost: v.authorityBoost!,
+      pillars: (v.pillars as BrandPillar[]).map((p) => ({
+        id: p.id, name: p.name, description: p.description, weight: p.weight, order: p.order,
+      })),
+      authorityTopics: splitLines(v.authorityTopics),
+      antiTopics: splitLines(v.antiTopics),
+      voiceMarkers: splitLines(v.voiceMarkers),
+      concurrencyToken: this.token,
+    };
+  }
+
+  private definitionsDiffer(): boolean {
+    const b = this.baseline;
+    if (!b) return false;
+    const v = this.form.getRawValue();
+    if (v.positioning !== b.positioning) return true;
+    if (v.audiencePrimary !== b.audiencePrimary) return true;
+    if ((v.audienceSecondary || null) !== (b.audienceSecondary || null)) return true;
+    if (!arraysEqual(splitLines(v.authorityTopics), b.authorityTopics)) return true;
+    if (!arraysEqual(splitLines(v.antiTopics), b.antiTopics)) return true;
+    if (!arraysEqual(splitLines(v.voiceMarkers), b.voiceMarkers)) return true;
+
+    const formPillars = v.pillars as BrandPillar[];
+    if (formPillars.length !== b.pillars.length) return true;
+    const baselineById = new Map(b.pillars.map((p) => [p.id, p]));
+    for (const fp of formPillars) {
+      const bp = baselineById.get(fp.id);
+      if (!bp || fp.name !== bp.name || fp.description !== bp.description) return true;
+    }
+    return false;
+  }
+
+  private weightsKnobsDiffer(): boolean {
+    const b = this.baseline;
+    if (!b) return false;
+    const v = this.form.getRawValue();
+    if (
+      v.halfLifeDays !== b.halfLifeDays ||
+      v.decayFloor !== b.decayFloor ||
+      v.antiTopicMultiplier !== b.antiTopicMultiplier ||
+      v.authorityBoost !== b.authorityBoost
+    ) {
+      return true;
+    }
+    const baselineById = new Map(b.pillars.map((p) => [p.id, p]));
+    for (const fp of v.pillars as BrandPillar[]) {
+      const bp = baselineById.get(fp.id);
+      if (bp && (fp.weight !== bp.weight || fp.order !== bp.order)) return true;
+    }
+    return false;
+  }
+
+  private applyServerState(profile: BrandRankingProfile, patchForm: boolean): void {
+    this.baseline = profile;
+    this.token = profile.concurrencyToken;
+    this.version.set(profile.version);
+    this.loading.set(false);
+    if (!patchForm) return;
+
+    this.ready = false;
+    this.pillars.clear();
+    for (const p of profile.pillars) this.pillars.push(this.buildPillarGroup(p));
+    this.form.patchValue(
+      {
+        positioning: profile.positioning,
+        audiencePrimary: profile.audiencePrimary,
+        audienceSecondary: profile.audienceSecondary ?? '',
+        halfLifeDays: profile.halfLifeDays,
+        decayFloor: profile.decayFloor,
+        antiTopicMultiplier: profile.antiTopicMultiplier,
+        authorityBoost: profile.authorityBoost,
+        authorityTopics: profile.authorityTopics.join('\n'),
+        antiTopics: profile.antiTopics.join('\n'),
+        voiceMarkers: profile.voiceMarkers.join('\n'),
+      },
+      { emitEvent: false }
+    );
+    this.definitionsDirty.set(false);
+    this.ready = true;
+  }
+
+  private buildPillarGroup(pillar: BrandPillar): FormGroup {
+    return this.fb.group({
+      id: [pillar.id],
+      name: [pillar.name, Validators.required],
+      description: [pillar.description],
+      weight: [pillar.weight, [Validators.min(0), Validators.max(1)]],
+      order: [pillar.order],
+    });
+  }
+
+  private handleError(err: { status?: number }): void {
+    this.conflictMessage.set(
+      err?.status === 409
+        ? 'This profile was changed elsewhere. Reload to continue.'
+        : 'Save failed. Please try again.'
+    );
+  }
+}
diff --git a/src/PersonalBrandAssistant.Web/src/app/models/brand-profile.model.ts b/src/PersonalBrandAssistant.Web/src/app/models/brand-profile.model.ts
new file mode 100644
index 0000000..2e1eb58
--- /dev/null
+++ b/src/PersonalBrandAssistant.Web/src/app/models/brand-profile.model.ts
@@ -0,0 +1,44 @@
+export interface BrandPillar {
+  id: string;
+  name: string;
+  description: string;
+  weight: number;
+  order: number;
+}
+
+export interface BrandRankingProfile {
+  id: string;
+  version: number;
+  positioning: string;
+  audiencePrimary: string;
+  audienceSecondary: string | null;
+  halfLifeDays: number;
+  decayFloor: number;
+  antiTopicMultiplier: number;
+  authorityBoost: number;
+  pillars: BrandPillar[];
+  authorityTopics: string[];
+  antiTopics: string[];
+  voiceMarkers: string[];
+  concurrencyToken: string;
+  updatedAt: string;
+}
+
+/**
+ * PUT body for /api/brand-ranking-profile. The server decides the write mode (weights-only vs
+ * definition) from the diff (R-H4) and round-trips concurrencyToken for optimistic concurrency.
+ */
+export interface UpdateBrandProfileRequest {
+  positioning: string;
+  audiencePrimary: string;
+  audienceSecondary: string | null;
+  halfLifeDays: number;
+  decayFloor: number;
+  antiTopicMultiplier: number;
+  authorityBoost: number;
+  pillars: BrandPillar[];
+  authorityTopics: string[];
+  antiTopics: string[];
+  voiceMarkers: string[];
+  concurrencyToken: string;
+}
