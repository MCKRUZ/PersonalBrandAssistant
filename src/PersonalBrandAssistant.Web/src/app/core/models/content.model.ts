import { PlatformType } from './platform.model';
import { AutonomyLevel } from './autonomy.model';

export type ContentStatus = 'Draft' | 'Review' | 'Approved' | 'Scheduled' | 'Publishing' | 'Published' | 'Failed' | 'Archived';
export type ContentType = 'BlogPost' | 'SocialPost' | 'Thread' | 'VideoDescription';

export interface ContentItem {
  id: string;
  title: string;
  body: string;
  type: ContentType;
  status: ContentStatus;
  platform: PlatformType;
  createdAt: string;
  updatedAt: string;
  scheduledAt?: string;
  version: number;
  capturedAutonomyLevel: AutonomyLevel;
}
