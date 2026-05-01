export interface BrandVoiceScore {
  overallScore: number;
  authoritative: number;
  pragmatic: number;
  concise: number;
  practitioner: number;
  issues: readonly string[];
  ruleViolations: readonly string[];
}
