import { CalendarSlotStatus, ContentType, PlatformType } from './enums';

export interface CalendarSlot {
  readonly id: string;
  readonly scheduledAt: string;
  readonly platform: PlatformType;
  readonly contentSeriesId?: string;
  readonly contentId?: string;
  readonly status: CalendarSlotStatus;
  readonly isOverride: boolean;
  readonly overriddenOccurrence?: string;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface ContentSeries {
  readonly id: string;
  readonly name: string;
  readonly description?: string;
  readonly recurrenceRule: string;
  readonly targetPlatforms: readonly PlatformType[];
  readonly contentType: ContentType;
  readonly themeTags: readonly string[];
  readonly timeZoneId: string;
  readonly startsAt: string;
  readonly endsAt?: string;
  readonly isActive: boolean;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface ContentSeriesRequest {
  readonly name: string;
  readonly description?: string;
  readonly recurrenceRule: string;
  readonly targetPlatforms: readonly PlatformType[];
  readonly contentType: ContentType;
  readonly themeTags: readonly string[];
  readonly timeZoneId: string;
  readonly startsAt: string;
  readonly endsAt?: string;
}

export interface CalendarSlotRequest {
  readonly scheduledAt: string;
  readonly platform: PlatformType;
}
