import { ContentStatus, ContentType } from '../models/content.model';

export function formatContentType(contentType: string): string {
  return contentType.replace(/([A-Z])/g, ' $1').trim();
}

/** status -> { color: cssVar, label, order }. order = pipeline rank (Idea 0 .. Archived 6). */
export const STATUS_META: Record<ContentStatus, { color: string; label: string; order: number }> = {
  [ContentStatus.Idea]: { color: 'var(--status-idea)', label: 'Idea', order: 0 },
  [ContentStatus.Draft]: { color: 'var(--status-draft)', label: 'Draft', order: 1 },
  [ContentStatus.Review]: { color: 'var(--status-review)', label: 'Review', order: 2 },
  [ContentStatus.Approved]: { color: 'var(--status-approved)', label: 'Approved', order: 3 },
  [ContentStatus.Scheduled]: { color: 'var(--status-scheduled)', label: 'Scheduled', order: 4 },
  [ContentStatus.Published]: { color: 'var(--status-published)', label: 'Published', order: 5 },
  [ContentStatus.Archived]: { color: 'var(--status-archived)', label: 'Archived', order: 6 },
};

/** ContentType -> single Unicode glyph for cards/rows. */
export const TYPE_GLYPH: Record<ContentType, string> = {
  [ContentType.BlogPost]: '¶',
  [ContentType.LinkedInPost]: '▤',
  [ContentType.Tweet]: '◇',
  [ContentType.ThreadedTweet]: '⋮',
  [ContentType.SubstackNewsletter]: '✉',
  [ContentType.RedditPost]: '▷',
  [ContentType.YouTubeVideo]: '▶',
  [ContentType.YouTubeShort]: '▹',
};

/** numeric voice score -> band color css var. >=80 high, >=60 mid, else low; null -> neutral. */
export function voiceBandColor(score: number | null): string {
  if (score === null) return 'var(--text-muted)';
  if (score >= 80) return 'var(--voice-high)';
  if (score >= 60) return 'var(--voice-mid)';
  return 'var(--voice-low)';
}

/** legal forward/backward status transitions, derived from the real state-machine endpoints.
 *  Archived restores via a dedicated endpoint; it carries no forward target here. */
export const LEGAL_TRANSITIONS: Record<ContentStatus, ContentStatus[]> = {
  [ContentStatus.Idea]: [ContentStatus.Draft],
  [ContentStatus.Draft]: [ContentStatus.Review, ContentStatus.Approved],
  [ContentStatus.Review]: [ContentStatus.Approved, ContentStatus.Draft],
  [ContentStatus.Approved]: [ContentStatus.Scheduled, ContentStatus.Published],
  [ContentStatus.Scheduled]: [ContentStatus.Approved, ContentStatus.Published],
  [ContentStatus.Published]: [ContentStatus.Approved],
  [ContentStatus.Archived]: [],
};

/** next status in pipeline order; null if Published or Archived (no forward move). */
export function nextStatus(current: ContentStatus): ContentStatus | null {
  const forward: Partial<Record<ContentStatus, ContentStatus>> = {
    [ContentStatus.Idea]: ContentStatus.Draft,
    [ContentStatus.Draft]: ContentStatus.Review,
    [ContentStatus.Review]: ContentStatus.Approved,
    [ContentStatus.Approved]: ContentStatus.Scheduled,
    [ContentStatus.Scheduled]: ContentStatus.Published,
  };
  return forward[current] ?? null;
}

/** compact relative time: past -> "2h"/"3d"; future -> "in 3d"; ~now -> "just now". */
export function relativeTime(iso: string): string {
  const then = new Date(iso).getTime();
  const now = Date.now();
  const diffMs = then - now;
  const future = diffMs > 0;
  const absSec = Math.floor(Math.abs(diffMs) / 1000);

  if (absSec < 60) return 'just now';

  const units: [number, string][] = [
    [86400, 'd'],
    [3600, 'h'],
    [60, 'm'],
  ];
  for (const [secs, label] of units) {
    if (absSec >= secs) {
      const n = Math.floor(absSec / secs);
      return future ? `in ${n}${label}` : `${n}${label}`;
    }
  }
  return 'just now';
}

export function voiceScoreClass(score: number | null): string {
  if (score === null) return 'voice-dot voice-none';
  if (score > 80) return 'voice-dot voice-green';
  if (score >= 60) return 'voice-dot voice-amber';
  return 'voice-dot voice-red';
}

export function platformIconClass(platform: string): string {
  const icons: Record<string, string> = {
    Blog: 'pi pi-globe',
    Medium: 'pi pi-book',
    LinkedIn: 'pi pi-linkedin',
    Twitter: 'pi pi-twitter',
    Substack: 'pi pi-envelope',
    Reddit: 'pi pi-comments',
    YouTube: 'pi pi-youtube',
  };
  return icons[platform] ?? 'pi pi-globe';
}

export function truncateText(text: string, maxLength: number): string {
  return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
}

import { PublishStatus } from '../models/content.model';

export function publishStatusSeverity(status: PublishStatus | string): 'success' | 'danger' | 'warn' | 'info' {
  switch (status) {
    case PublishStatus.Published: return 'success';
    case PublishStatus.Failed: return 'danger';
    case PublishStatus.Pending:
    case PublishStatus.Formatting: return 'warn';
    default: return 'info';
  }
}
