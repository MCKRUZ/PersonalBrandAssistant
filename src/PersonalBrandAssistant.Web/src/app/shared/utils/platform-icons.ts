import { PlatformType } from '../models';

export const PLATFORM_ICONS: Readonly<Record<PlatformType, string>> = {
  TwitterX: 'pi pi-twitter',
  LinkedIn: 'pi pi-linkedin',
  Instagram: 'pi pi-instagram',
  YouTube: 'pi pi-youtube',
  Reddit: 'pi pi-reddit',
};

export const PLATFORM_LABELS: Readonly<Record<PlatformType, string>> = {
  TwitterX: 'Twitter/X',
  LinkedIn: 'LinkedIn',
  Instagram: 'Instagram',
  YouTube: 'YouTube',
  Reddit: 'Reddit',
};

export const PLATFORM_COLORS: Readonly<Record<PlatformType, string>> = {
  TwitterX: '#1DA1F2',
  LinkedIn: '#0A66C2',
  Instagram: '#E4405F',
  YouTube: '#FF0000',
  Reddit: '#FF4500',
};
