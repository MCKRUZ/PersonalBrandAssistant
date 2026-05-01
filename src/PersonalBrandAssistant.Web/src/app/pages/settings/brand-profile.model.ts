export interface ToneSlider {
  readonly left: string;
  readonly right: string;
  readonly value: number;
}

export interface BrandProfile {
  readonly toneSliders: readonly ToneSlider[];
  readonly vocabularyPreferences: { readonly preferredTerms: readonly string[]; readonly avoidTerms: readonly string[] };
  readonly pillars: readonly BrandPillar[];
  readonly guardrails: readonly string[];
}

export interface BrandPillar {
  readonly name: string;
  readonly description: string;
  readonly active: boolean;
}

export const DEFAULT_TONE_SLIDERS: ToneSlider[] = [
  { left: 'Authoritative', right: 'Casual', value: 50 },
  { left: 'Technical', right: 'Accessible', value: 50 },
  { left: 'Formal', right: 'Conversational', value: 50 },
  { left: 'Detailed', right: 'Concise', value: 50 },
  { left: 'Serious', right: 'Humorous', value: 50 },
  { left: 'Innovative', right: 'Traditional', value: 50 },
  { left: 'Direct', right: 'Diplomatic', value: 50 },
  { left: 'Expert', right: 'Beginner-friendly', value: 50 },
];

export const DEFAULT_BRAND_PROFILE: BrandProfile = {
  toneSliders: DEFAULT_TONE_SLIDERS,
  vocabularyPreferences: { preferredTerms: [], avoidTerms: [] },
  pillars: [],
  guardrails: [],
};
