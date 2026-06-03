import { Platform, PUBLISHABLE_PLATFORMS } from './content.model';

export type DeliveryMode = 'auto' | 'manual';

export interface PlatformMeta {
  /** 2-letter tile code, e.g. "Bl", "Me". */
  code: string;
  label: string;
  delivery: DeliveryMode;
  /** Character cap, or null when the platform has no limit. */
  charLimit: number | null;
  /** Human-readable format note. */
  fmt: string;
}

/** Static design metadata for the publishable platforms. Connection state is runtime (getPlatforms). */
export const PLATFORM_META: Record<Platform, PlatformMeta> = {
  [Platform.Blog]: {
    code: 'Bl',
    label: 'Blog',
    delivery: 'auto',
    charLimit: null,
    fmt: 'Markdown + HTML, images, full formatting',
  },
  [Platform.Medium]: {
    code: 'Me',
    label: 'Medium',
    delivery: 'manual',
    charLimit: null,
    fmt: 'No publish API — paste into Medium',
  },
  [Platform.Substack]: {
    code: 'Su',
    label: 'Substack',
    delivery: 'auto',
    charLimit: null,
    fmt: 'Newsletter — sends to subscribers automatically',
  },
  [Platform.LinkedIn]: {
    code: 'Li',
    label: 'LinkedIn',
    delivery: 'auto',
    charLimit: 3000,
    fmt: 'Posts automatically to your profile',
  },
  [Platform.Twitter]: {
    code: 'Tw',
    label: 'Twitter',
    delivery: 'auto',
    charLimit: 280,
    fmt: 'Splits into a numbered thread',
  },
  // Non-publishable platforms — present for type completeness; not shown in the publish modal.
  [Platform.Reddit]: { code: 'Re', label: 'Reddit', delivery: 'manual', charLimit: 40000, fmt: 'Manual post' },
  [Platform.YouTube]: { code: 'Yt', label: 'YouTube', delivery: 'manual', charLimit: null, fmt: 'Manual upload' },
};

export interface DeliveryBadge {
  text: string;
  variant: 'auto' | 'warn' | 'manual';
}

/**
 * Badge derived from STATIC delivery + LIVE connection:
 *  - auto + connected  -> "⚡ Auto-publish" (auto)
 *  - auto + !connected -> "⚡ Connect to auto-publish" (warn)
 *  - manual            -> "✋ Manual" (manual)
 */
export function deliveryBadge(meta: PlatformMeta, isConnected: boolean): DeliveryBadge {
  if (meta.delivery === 'manual') return { text: '✋ Manual', variant: 'manual' };
  return isConnected
    ? { text: '⚡ Auto-publish', variant: 'auto' }
    : { text: '⚡ Connect to auto-publish', variant: 'warn' };
}

export { PUBLISHABLE_PLATFORMS };
