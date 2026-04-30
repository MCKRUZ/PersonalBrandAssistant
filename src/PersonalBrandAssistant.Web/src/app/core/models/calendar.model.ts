import { PlatformType } from './platform.model';
import { ContentStatus } from './content.model';

export interface CalendarSlot {
  id: string;
  scheduledAt: string;
  platform: PlatformType;
  contentId?: string;
  contentTitle?: string;
  contentStatus?: ContentStatus;
}
