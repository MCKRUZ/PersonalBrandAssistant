export interface BrandPillar {
  id: string;
  name: string;
  description: string;
  weight: number;
  order: number;
}

export interface BrandRankingProfile {
  id: string;
  version: number;
  positioning: string;
  audiencePrimary: string;
  audienceSecondary: string | null;
  halfLifeDays: number;
  decayFloor: number;
  antiTopicMultiplier: number;
  authorityBoost: number;
  pillars: BrandPillar[];
  authorityTopics: string[];
  antiTopics: string[];
  voiceMarkers: string[];
  concurrencyToken: string;
  updatedAt: string;
}

/**
 * PUT body for /api/brand-ranking-profile. The server decides the write mode (weights-only vs
 * definition) from the diff (R-H4) and round-trips concurrencyToken for optimistic concurrency.
 */
export interface UpdateBrandProfileRequest {
  positioning: string;
  audiencePrimary: string;
  audienceSecondary: string | null;
  halfLifeDays: number;
  decayFloor: number;
  antiTopicMultiplier: number;
  authorityBoost: number;
  pillars: BrandPillar[];
  authorityTopics: string[];
  antiTopics: string[];
  voiceMarkers: string[];
  concurrencyToken: string;
}
