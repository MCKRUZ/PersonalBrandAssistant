export interface AutonomySettings {
  readonly id: string;
  readonly globalLevel: AutonomyLevel;
  readonly autoPublishEnabled: boolean;
  readonly requireApprovalForSocial: boolean;
  readonly maxAutoPostsPerDay: number;
  readonly defaultTone: string;
  readonly autoScheduleEnabled: boolean;
}

export type AutonomyLevel = 'Manual' | 'Assisted' | 'SemiAuto' | 'Autonomous';

export interface UpdateAutonomySettingsRequest {
  readonly globalLevel: AutonomyLevel;
  readonly autoPublishEnabled: boolean;
  readonly requireApprovalForSocial: boolean;
  readonly maxAutoPostsPerDay: number;
  readonly defaultTone: string;
  readonly autoScheduleEnabled: boolean;
}
