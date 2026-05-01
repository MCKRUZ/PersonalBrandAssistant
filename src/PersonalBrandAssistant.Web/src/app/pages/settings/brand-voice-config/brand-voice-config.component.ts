import { Component, effect, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { SliderModule } from 'primeng/slider';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { Chip } from 'primeng/chip';
import { TableModule } from 'primeng/table';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { BrandProfile, BrandPillar, ToneSlider, DEFAULT_BRAND_PROFILE } from '../brand-profile.model';

@Component({
  selector: 'app-brand-voice-config',
  standalone: true,
  imports: [CommonModule, FormsModule, CardModule, SliderModule, InputTextModule, ButtonModule, Chip, TableModule, ToggleSwitchModule],
  template: `
    <p-card header="Brand Voice">
      <div class="brand-voice-config">
        <!-- Tone Sliders -->
        <section class="section">
          <h4>Tone Dimensions</h4>
          @for (slider of toneSliders(); track slider.left; let i = $index) {
            <div class="tone-row">
              <span class="tone-label left">{{ slider.left }}</span>
              <p-slider [ngModel]="slider.value" (ngModelChange)="onToneChange(i, $event)" [min]="0" [max]="100" styleClass="flex-1" />
              <span class="tone-label right">{{ slider.right }}</span>
            </div>
          }
        </section>

        <!-- Vocabulary Pills -->
        <section class="section">
          <h4>Always Use</h4>
          <div class="chip-list">
            @for (term of preferredTerms(); track term; let i = $index) {
              <p-chip [label]="term" [removable]="true" (onRemove)="removePreferred(i)" />
            }
          </div>
          <div class="add-row">
            <input pInputText [(ngModel)]="newPreferred" placeholder="Add term..." class="flex-1" />
            <p-button icon="pi pi-plus" [text]="true" (onClick)="addPreferred()" [disabled]="!newPreferred" />
          </div>

          <h4>Never Use</h4>
          <div class="chip-list">
            @for (term of avoidTerms(); track term; let i = $index) {
              <p-chip [label]="term" [removable]="true" (onRemove)="removeAvoided(i)" />
            }
          </div>
          <div class="add-row">
            <input pInputText [(ngModel)]="newAvoided" placeholder="Add term..." class="flex-1" />
            <p-button icon="pi pi-plus" [text]="true" (onClick)="addAvoided()" [disabled]="!newAvoided" />
          </div>
        </section>

        <!-- Pillars -->
        <section class="section">
          <h4>Content Pillars</h4>
          <p-table [value]="$any(pillars())" styleClass="p-datatable-sm">
            <ng-template #header>
              <tr>
                <th>Name</th>
                <th>Description</th>
                <th style="width: 5rem">Active</th>
                <th style="width: 3rem"></th>
              </tr>
            </ng-template>
            <ng-template #body let-pillar let-i="rowIndex">
              <tr>
                <td><input pInputText [ngModel]="pillar.name" (ngModelChange)="updatePillar(i, 'name', $event)" class="w-full" /></td>
                <td><input pInputText [ngModel]="pillar.description" (ngModelChange)="updatePillar(i, 'description', $event)" class="w-full" /></td>
                <td><p-toggleSwitch [ngModel]="pillar.active" (ngModelChange)="updatePillar(i, 'active', $event)" /></td>
                <td><p-button icon="pi pi-trash" [text]="true" severity="danger" (onClick)="removePillar(i)" /></td>
              </tr>
            </ng-template>
          </p-table>
          <p-button label="Add Pillar" icon="pi pi-plus" [text]="true" (onClick)="addPillar()" />
        </section>

        <!-- Guardrails -->
        <section class="section">
          <h4>Guardrails</h4>
          @for (rule of guardrails(); track $index; let i = $index) {
            <div class="guardrail-row">
              <span class="flex-1">{{ rule }}</span>
              <p-button icon="pi pi-times" [text]="true" severity="danger" (onClick)="removeGuardrail(i)" />
            </div>
          }
          <div class="add-row">
            <input pInputText [(ngModel)]="newGuardrail" placeholder="Add guardrail rule..." class="flex-1" />
            <p-button icon="pi pi-plus" [text]="true" (onClick)="addGuardrail()" [disabled]="!newGuardrail" />
          </div>
        </section>

        <div class="actions">
          <p-button label="Save" icon="pi pi-check" (onClick)="save()" />
          <p-button label="Reset" icon="pi pi-undo" [text]="true" (onClick)="reset()" />
        </div>
      </div>
    </p-card>
  `,
  styles: `
    .brand-voice-config { display: flex; flex-direction: column; gap: 1.5rem; }
    .section h4 { margin: 0 0 0.75rem; font-weight: 600; }
    .tone-row { display: flex; align-items: center; gap: 1rem; margin-bottom: 0.75rem; }
    .tone-label { width: 8rem; font-size: 0.875rem; color: var(--p-text-muted-color); }
    .tone-label.right { text-align: right; }
    .chip-list { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-bottom: 0.5rem; }
    .add-row { display: flex; gap: 0.5rem; align-items: center; }
    .guardrail-row { display: flex; align-items: center; gap: 0.5rem; padding: 0.375rem 0; border-bottom: 1px solid var(--p-surface-border); }
    .actions { display: flex; gap: 0.5rem; padding-top: 0.5rem; }
    .flex-1 { flex: 1; }
  `,
})
export class BrandVoiceConfigComponent {
  readonly profile = input<BrandProfile | undefined>();
  readonly profileChange = output<BrandProfile>();

  readonly toneSliders = signal<ToneSlider[]>([]);
  readonly preferredTerms = signal<string[]>([]);
  readonly avoidTerms = signal<string[]>([]);
  readonly pillars = signal<BrandPillar[]>([]);
  readonly guardrails = signal<string[]>([]);

  newPreferred = '';
  newAvoided = '';
  newGuardrail = '';

  constructor() {
    effect(() => {
      const p = this.profile();
      if (p) this.hydrateFrom(p);
    });
    this.hydrateFrom(DEFAULT_BRAND_PROFILE);
  }

  private hydrateFrom(p: BrandProfile) {
    this.toneSliders.set([...p.toneSliders]);
    this.preferredTerms.set([...p.vocabularyPreferences.preferredTerms]);
    this.avoidTerms.set([...p.vocabularyPreferences.avoidTerms]);
    this.pillars.set([...p.pillars]);
    this.guardrails.set([...p.guardrails]);
  }

  onToneChange(index: number, value: number) {
    const sliders = [...this.toneSliders()];
    sliders[index] = { ...sliders[index], value };
    this.toneSliders.set(sliders);
  }

  addPreferred() {
    if (this.newPreferred.trim()) {
      this.preferredTerms.set([...this.preferredTerms(), this.newPreferred.trim()]);
      this.newPreferred = '';
    }
  }

  removePreferred(index: number) {
    this.preferredTerms.set(this.preferredTerms().filter((_, i) => i !== index));
  }

  addAvoided() {
    if (this.newAvoided.trim()) {
      this.avoidTerms.set([...this.avoidTerms(), this.newAvoided.trim()]);
      this.newAvoided = '';
    }
  }

  removeAvoided(index: number) {
    this.avoidTerms.set(this.avoidTerms().filter((_, i) => i !== index));
  }

  addPillar() {
    this.pillars.set([...this.pillars(), { name: '', description: '', active: true }]);
  }

  removePillar(index: number) {
    this.pillars.set(this.pillars().filter((_, i) => i !== index));
  }

  updatePillar(index: number, field: keyof BrandPillar, value: string | boolean) {
    const updated = [...this.pillars()];
    updated[index] = { ...updated[index], [field]: value };
    this.pillars.set(updated);
  }

  addGuardrail() {
    if (this.newGuardrail.trim()) {
      this.guardrails.set([...this.guardrails(), this.newGuardrail.trim()]);
      this.newGuardrail = '';
    }
  }

  removeGuardrail(index: number) {
    this.guardrails.set(this.guardrails().filter((_, i) => i !== index));
  }

  save() {
    this.profileChange.emit({
      toneSliders: this.toneSliders(),
      vocabularyPreferences: { preferredTerms: this.preferredTerms(), avoidTerms: this.avoidTerms() },
      pillars: this.pillars(),
      guardrails: this.guardrails(),
    });
  }

  reset() {
    this.hydrateFrom(this.profile() ?? DEFAULT_BRAND_PROFILE);
  }
}
