export type AutonomyLevel = 'Manual' | 'Suggest' | 'Draft' | 'AutoPublish' | 'FullAuto';

export interface AutonomySettings {
  readonly globalLevel: AutonomyLevel;
  readonly autoPublishThreshold: number;
}
