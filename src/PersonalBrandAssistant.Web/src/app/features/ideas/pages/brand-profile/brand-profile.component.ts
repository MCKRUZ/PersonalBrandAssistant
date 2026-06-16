import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { debounceTime } from 'rxjs';
import { ConfirmationService } from 'primeng/api';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { SliderModule } from 'primeng/slider';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { InputNumberModule } from 'primeng/inputnumber';
import { ButtonModule } from 'primeng/button';
import { IdeaService } from '../../../../core/services/idea.service';
import { IdeaStore } from '../../store/idea.store';
import {
  BrandPillar,
  BrandRankingProfile,
  UpdateBrandProfileRequest,
} from '../../../../models/brand-profile.model';

const positive = (c: AbstractControl) => (c.value > 0 ? null : { positive: true });

const minOnePillar = (c: AbstractControl) =>
  (c as FormArray).length >= 1 ? null : { minPillars: true };

const splitLines = (s: string | null | undefined): string[] =>
  (s ?? '')
    .split('\n')
    .map((t) => t.trim())
    .filter((t) => t.length > 0);

const arraysEqual = (a: string[], b: string[]): boolean =>
  a.length === b.length && a.every((v, i) => v === b[i]);

@Component({
  selector: 'app-brand-profile',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ConfirmDialogModule,
    SliderModule,
    InputTextModule,
    TextareaModule,
    InputNumberModule,
    ButtonModule,
  ],
  providers: [ConfirmationService],
  template: `
    <div class="brand-profile">
      <h2>Brand Ranking Profile <small>v{{ version() }}</small></h2>

      @if (conflictMessage(); as msg) {
        <div class="conflict" data-testid="conflict-message">{{ msg }}</div>
      }
      @if (saveNotice(); as note) {
        <div class="notice" data-testid="save-notice">{{ note }}</div>
      }

      @if (!loading() && hasProfile()) {
        <form [formGroup]="form">
          <section>
            <h3>Positioning &amp; audience</h3>
            <label>Positioning
              <input type="text" pInputText formControlName="positioning" data-testid="positioning" />
            </label>
            <label>Primary audience
              <input type="text" pInputText formControlName="audiencePrimary" data-testid="audience-primary" />
            </label>
            <label>Secondary audience
              <input type="text" pInputText formControlName="audienceSecondary" />
            </label>
          </section>

          <section>
            <h3>Ranking knobs (auto-applied)</h3>
            <label>Half-life (days)
              <p-inputNumber formControlName="halfLifeDays" [minFractionDigits]="0" [maxFractionDigits]="2" />
            </label>
            <label>Decay floor
              <p-inputNumber formControlName="decayFloor" [minFractionDigits]="0" [maxFractionDigits]="3" />
            </label>
            <label>Anti-topic multiplier
              <p-inputNumber formControlName="antiTopicMultiplier" [minFractionDigits]="0" [maxFractionDigits]="3" />
            </label>
            <label>Authority boost
              <p-inputNumber formControlName="authorityBoost" [minFractionDigits]="0" [maxFractionDigits]="3" />
            </label>
          </section>

          <section formArrayName="pillars">
            <h3>Pillars</h3>
            @for (pillar of pillars.controls; track pillar; let i = $index) {
              <div class="pillar" [formGroupName]="i">
                <input type="text" pInputText formControlName="name"
                       [attr.data-testid]="'pillar-name-' + i" placeholder="Name" />
                <textarea pTextarea formControlName="description"
                          [attr.data-testid]="'pillar-desc-' + i" placeholder="Description"></textarea>
                <p-slider formControlName="weight" [min]="0" [max]="1" [step]="0.05"
                          [attr.aria-label]="'Weight for pillar ' + (i + 1)"
                          [attr.data-testid]="'pillar-weight-' + i" />
                <button type="button" pButton severity="secondary" (click)="removePillar(i)"
                        [attr.aria-label]="'Remove pillar ' + (i + 1)"
                        [attr.data-testid]="'pillar-remove-' + i">Remove</button>
              </div>
            }
            <button type="button" pButton (click)="addPillar()" aria-label="Add pillar"
                    data-testid="pillar-add">Add pillar</button>
          </section>

          <section>
            <h3>Topics &amp; voice (one per line — definition changes)</h3>
            <label>Authority topics
              <textarea pTextarea formControlName="authorityTopics" data-testid="authority-topics"></textarea>
            </label>
            <label>Anti topics
              <textarea pTextarea formControlName="antiTopics" data-testid="anti-topics"></textarea>
            </label>
            <label>Voice markers
              <textarea pTextarea formControlName="voiceMarkers" data-testid="voice-markers"></textarea>
            </label>
          </section>

          <button type="button" pButton (click)="saveDefinitions()"
                  [disabled]="form.invalid || !definitionsDirty()" data-testid="save-rescore">
            Save &amp; re-score
          </button>
        </form>
      }

      <p-confirmDialog />
    </div>
  `,
})
export class BrandProfileComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly ideaService = inject(IdeaService);
  private readonly ideaStore = inject(IdeaStore);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly version = signal(0);
  readonly loading = signal(true);
  readonly hasProfile = signal(false);
  readonly definitionsDirty = signal(false);
  readonly conflictMessage = signal<string | null>(null);
  readonly saveNotice = signal<string | null>(null);

  private baseline: BrandRankingProfile | null = null;
  private token = '';
  private ready = false; // suppresses auto-apply while we patch the form programmatically

  readonly form = this.fb.group({
    positioning: ['', Validators.required],
    audiencePrimary: ['', Validators.required],
    audienceSecondary: [''],
    halfLifeDays: [7, [Validators.required, positive]],
    decayFloor: [0.075, [Validators.required, Validators.min(0), Validators.max(1)]],
    antiTopicMultiplier: [0.1, [Validators.required, positive]],
    authorityBoost: [1.2, [Validators.required, positive]],
    authorityTopics: [''],
    antiTopics: [''],
    voiceMarkers: [''],
    pillars: this.fb.array<FormGroup>([], minOnePillar),
  });

  get pillars(): FormArray {
    return this.form.get('pillars') as FormArray;
  }

  ngOnInit(): void {
    this.ideaService.getBrandProfile().subscribe({
      next: (profile) => this.applyServerState(profile, true),
      error: () => {
        this.loading.set(false);
        this.conflictMessage.set('Failed to load the brand profile.');
      },
    });

    this.form.valueChanges
      .pipe(debounceTime(400), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.ready || !this.baseline) return;
        const defsDirty = this.definitionsDiffer();
        this.definitionsDirty.set(defsDirty);
        // Weight/knob edits auto-apply as a weights-only PUT (no version bump, no re-score) ONLY when no
        // definition edit is staged. Once ANY definition diff is pending (including an added/removed pillar
        // or a reorder), every change routes through the explicit "Save & re-score" confirm — a staged
        // definition edit can never ride along on, or be silently dropped by, a no-confirm auto-apply.
        if (this.form.valid && !defsDirty && this.weightsKnobsDiffer()) {
          this.applyWeightsOnly();
        }
      });
  }

  addPillar(): void {
    this.pillars.push(this.buildPillarGroup({
      id: crypto.randomUUID(), name: '', description: '', weight: 0, order: this.pillars.length,
    }));
  }

  removePillar(index: number): void {
    this.pillars.removeAt(index);
  }

  saveDefinitions(): void {
    if (this.form.invalid) return;
    this.confirmationService.confirm({
      header: 'Save & re-score',
      message:
        'Re-scoring all recent ideas against the updated profile will spend LLM tokens. Continue?',
      accept: () => {
        this.conflictMessage.set(null);
        this.ideaService.updateBrandProfile(this.buildDefinitionRequest()).subscribe({
          next: (profile) => {
            this.applyServerState(profile, true);
            this.saveNotice.set('Re-score queued.');
          },
          error: (err) => this.handleError(err),
        });
      },
    });
  }

  private applyWeightsOnly(): void {
    this.conflictMessage.set(null);
    const priorVersion = this.baseline!.version;
    this.ideaService.updateBrandProfile(this.buildWeightsOnlyRequest()).subscribe({
      next: (profile) => {
        this.applyServerState(profile, false); // refresh baseline/token; keep the user's in-flight edits
        // Defense in depth: a weights-only apply must NOT bump the version. If the server escalated to a
        // re-score, tell the user rather than silently spending tokens.
        if (profile.version > priorVersion) this.saveNotice.set('Profile was re-scored.');
        this.ideaStore.loadIdeas();
      },
      error: (err) => {
        this.handleError(err);
        // On a stale-token conflict, resync from the server so the token + form reflect truth — never
        // blind-retry the stale token on the next keystroke.
        if (err?.status === 409) this.reloadFromServer();
      },
    });
  }

  private reloadFromServer(): void {
    this.ideaService.getBrandProfile().subscribe({
      next: (profile) => this.applyServerState(profile, true),
      error: () => {},
    });
  }

  private buildWeightsOnlyRequest(): UpdateBrandProfileRequest {
    const b = this.baseline!;
    const v = this.form.getRawValue();
    const formById = new Map(
      (v.pillars as BrandPillar[]).map((p) => [p.id, p])
    );
    return {
      positioning: b.positioning,
      audiencePrimary: b.audiencePrimary,
      audienceSecondary: b.audienceSecondary,
      halfLifeDays: v.halfLifeDays!,
      decayFloor: v.decayFloor!,
      antiTopicMultiplier: v.antiTopicMultiplier!,
      authorityBoost: v.authorityBoost!,
      // Baseline pillar set + names/descriptions, with weight/order from the form (no adds/removes).
      pillars: b.pillars.map((bp) => {
        const fp = formById.get(bp.id);
        return {
          id: bp.id, name: bp.name, description: bp.description,
          weight: fp ? fp.weight : bp.weight, order: fp ? fp.order : bp.order,
        };
      }),
      authorityTopics: b.authorityTopics,
      antiTopics: b.antiTopics,
      voiceMarkers: b.voiceMarkers,
      concurrencyToken: this.token,
    };
  }

  private buildDefinitionRequest(): UpdateBrandProfileRequest {
    const v = this.form.getRawValue();
    return {
      positioning: v.positioning!,
      audiencePrimary: v.audiencePrimary!,
      audienceSecondary: v.audienceSecondary || null,
      halfLifeDays: v.halfLifeDays!,
      decayFloor: v.decayFloor!,
      antiTopicMultiplier: v.antiTopicMultiplier!,
      authorityBoost: v.authorityBoost!,
      pillars: (v.pillars as BrandPillar[]).map((p) => ({
        id: p.id, name: p.name, description: p.description, weight: p.weight, order: p.order,
      })),
      authorityTopics: splitLines(v.authorityTopics),
      antiTopics: splitLines(v.antiTopics),
      voiceMarkers: splitLines(v.voiceMarkers),
      concurrencyToken: this.token,
    };
  }

  private definitionsDiffer(): boolean {
    const b = this.baseline;
    if (!b) return false;
    const v = this.form.getRawValue();
    if (v.positioning !== b.positioning) return true;
    if (v.audiencePrimary !== b.audiencePrimary) return true;
    if ((v.audienceSecondary || null) !== (b.audienceSecondary || null)) return true;
    if (!arraysEqual(splitLines(v.authorityTopics), b.authorityTopics)) return true;
    if (!arraysEqual(splitLines(v.antiTopics), b.antiTopics)) return true;
    if (!arraysEqual(splitLines(v.voiceMarkers), b.voiceMarkers)) return true;

    const formPillars = v.pillars as BrandPillar[];
    if (formPillars.length !== b.pillars.length) return true;
    const baselineById = new Map(b.pillars.map((p) => [p.id, p]));
    for (const fp of formPillars) {
      const bp = baselineById.get(fp.id);
      if (!bp || fp.name !== bp.name || fp.description !== bp.description) return true;
    }
    return false;
  }

  private weightsKnobsDiffer(): boolean {
    const b = this.baseline;
    if (!b) return false;
    const v = this.form.getRawValue();
    if (
      v.halfLifeDays !== b.halfLifeDays ||
      v.decayFloor !== b.decayFloor ||
      v.antiTopicMultiplier !== b.antiTopicMultiplier ||
      v.authorityBoost !== b.authorityBoost
    ) {
      return true;
    }
    // Only `weight` is a query-time (weights-only) field. A pillar reorder changes meaning and is treated
    // as a definition edit (via definitionsDiffer's pillar-set comparison), so `order` is NOT compared here.
    const baselineById = new Map(b.pillars.map((p) => [p.id, p]));
    for (const fp of v.pillars as BrandPillar[]) {
      const bp = baselineById.get(fp.id);
      if (bp && fp.weight !== bp.weight) return true;
    }
    return false;
  }

  private applyServerState(profile: BrandRankingProfile, patchForm: boolean): void {
    this.baseline = profile;
    this.token = profile.concurrencyToken;
    this.version.set(profile.version);
    this.loading.set(false);
    this.hasProfile.set(true);
    if (!patchForm) return;

    this.ready = false;
    this.pillars.clear();
    for (const p of profile.pillars) this.pillars.push(this.buildPillarGroup(p));
    this.form.patchValue(
      {
        positioning: profile.positioning,
        audiencePrimary: profile.audiencePrimary,
        audienceSecondary: profile.audienceSecondary ?? '',
        halfLifeDays: profile.halfLifeDays,
        decayFloor: profile.decayFloor,
        antiTopicMultiplier: profile.antiTopicMultiplier,
        authorityBoost: profile.authorityBoost,
        authorityTopics: profile.authorityTopics.join('\n'),
        antiTopics: profile.antiTopics.join('\n'),
        voiceMarkers: profile.voiceMarkers.join('\n'),
      },
      { emitEvent: false }
    );
    this.definitionsDirty.set(false);
    this.ready = true;
  }

  private buildPillarGroup(pillar: BrandPillar): FormGroup {
    return this.fb.group({
      id: [pillar.id],
      name: [pillar.name, Validators.required],
      description: [pillar.description],
      weight: [pillar.weight, [Validators.min(0), Validators.max(1)]],
      order: [pillar.order],
    });
  }

  private handleError(err: { status?: number }): void {
    this.conflictMessage.set(
      err?.status === 409
        ? 'This profile was changed elsewhere. Reload to continue.'
        : 'Save failed. Please try again.'
    );
  }
}
