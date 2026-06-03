import { PLATFORM_META, PUBLISHABLE_PLATFORMS, deliveryBadge } from './platform-metadata';
import { Platform } from './content.model';

describe('platform-metadata', () => {
  it('has an entry per publishable platform with the right delivery and char limits', () => {
    for (const p of PUBLISHABLE_PLATFORMS) {
      expect(PLATFORM_META[p]).toBeDefined();
      expect(PLATFORM_META[p].code.length).toBe(2);
    }
    expect(PLATFORM_META[Platform.Blog].delivery).toBe('auto');
    expect(PLATFORM_META[Platform.Blog].charLimit).toBeNull();
    expect(PLATFORM_META[Platform.Medium].delivery).toBe('manual');
    expect(PLATFORM_META[Platform.Substack].delivery).toBe('auto');
    expect(PLATFORM_META[Platform.LinkedIn].charLimit).toBe(3000);
    expect(PLATFORM_META[Platform.Twitter].charLimit).toBe(280);
  });

  describe('deliveryBadge', () => {
    it('auto + connected -> Auto-publish', () => {
      const b = deliveryBadge(PLATFORM_META[Platform.LinkedIn], true);
      expect(b.variant).toBe('auto');
      expect(b.text).toContain('Auto-publish');
    });
    it('auto + not connected -> Connect to auto-publish (warn)', () => {
      const b = deliveryBadge(PLATFORM_META[Platform.Twitter], false);
      expect(b.variant).toBe('warn');
      expect(b.text).toContain('Connect');
    });
    it('manual -> Manual regardless of connection', () => {
      expect(deliveryBadge(PLATFORM_META[Platform.Medium], true).variant).toBe('manual');
      expect(deliveryBadge(PLATFORM_META[Platform.Medium], false).variant).toBe('manual');
    });
  });
});
