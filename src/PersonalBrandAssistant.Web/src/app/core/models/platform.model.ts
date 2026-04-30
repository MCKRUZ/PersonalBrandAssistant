export type PlatformType = 'TwitterX' | 'LinkedIn' | 'Instagram' | 'YouTube' | 'Reddit' | 'PersonalBlog' | 'Substack';

export interface Platform {
  type: PlatformType;
  displayName: string;
  connected: boolean;
  lastSyncAt?: string;
  grantedScopes: readonly string[];
}
