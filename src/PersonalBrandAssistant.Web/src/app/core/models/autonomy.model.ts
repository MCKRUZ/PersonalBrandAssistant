export type AutonomyLevel = 'Manual' | 'Suggest' | 'Draft' | 'AutoPublish' | 'FullAuto';

export interface AutonomySettings {
  level: AutonomyLevel;
  autoPublishThreshold: number;
}
