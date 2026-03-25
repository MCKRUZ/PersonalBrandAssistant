import { PlatformType } from '../models';

export const PLATFORM_ICONS: Readonly<Record<PlatformType, string>> = {
  TwitterX: 'pi pi-twitter',
  LinkedIn: 'pi pi-linkedin',
  Instagram: 'pi pi-instagram',
  YouTube: 'pi pi-youtube',
  Reddit: 'pi pi-reddit',
  PersonalBlog: 'pi pi-globe',
  Substack: 'pi pi-at',
};

export const PLATFORM_LABELS: Readonly<Record<PlatformType, string>> = {
  TwitterX: 'Twitter/X',
  LinkedIn: 'LinkedIn',
  Instagram: 'Instagram',
  YouTube: 'YouTube',
  Reddit: 'Reddit',
  PersonalBlog: 'matthewkruczek.ai',
  Substack: 'Substack',
};

export const PLATFORM_COLORS: Readonly<Record<PlatformType, string>> = {
  TwitterX: '#1DA1F2',
  LinkedIn: '#0A66C2',
  Instagram: '#E4405F',
  YouTube: '#FF0000',
  Reddit: '#FF4500',
  PersonalBlog: '#8b5cf6',
  Substack: '#ff6719',
};
