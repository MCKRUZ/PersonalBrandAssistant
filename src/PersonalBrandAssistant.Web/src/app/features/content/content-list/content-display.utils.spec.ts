import {
  STATUS_META,
  TYPE_GLYPH,
  voiceBandColor,
  LEGAL_TRANSITIONS,
  nextStatus,
  relativeTime,
} from './content-display.utils';
import { ContentStatus, ContentType } from '../models/content.model';

describe('content-display.utils', () => {
  describe('STATUS_META', () => {
    it('has an entry for every ContentStatus with a css-var color and unique increasing order', () => {
      const statuses = Object.values(ContentStatus);
      for (const s of statuses) {
        expect(STATUS_META[s]).toBeDefined();
        expect(STATUS_META[s].color.startsWith('var(--')).toBeTrue();
      }
      const orders = statuses.map((s) => STATUS_META[s].order);
      expect(new Set(orders).size).toBe(orders.length); // unique
      // pipeline order ascending
      expect(STATUS_META[ContentStatus.Idea].order).toBeLessThan(STATUS_META[ContentStatus.Draft].order);
      expect(STATUS_META[ContentStatus.Approved].order).toBeLessThan(
        STATUS_META[ContentStatus.Published].order
      );
    });
  });

  describe('TYPE_GLYPH', () => {
    it('has a glyph for every ContentType', () => {
      for (const t of Object.values(ContentType)) {
        expect(TYPE_GLYPH[t]).toBeTruthy();
      }
    });
  });

  describe('voiceBandColor', () => {
    it('uses the >=80 boundary for the high band (not >80)', () => {
      expect(voiceBandColor(85)).toBe('var(--voice-high)');
      expect(voiceBandColor(80)).toBe('var(--voice-high)');
    });
    it('returns mid for >=60 and low below', () => {
      expect(voiceBandColor(60)).toBe('var(--voice-mid)');
      expect(voiceBandColor(59)).toBe('var(--voice-low)');
    });
    it('returns a neutral token for null', () => {
      expect(voiceBandColor(null)).toBe('var(--text-muted)');
    });
  });

  describe('LEGAL_TRANSITIONS', () => {
    it('encodes the state machine', () => {
      expect(LEGAL_TRANSITIONS[ContentStatus.Idea]).toEqual([ContentStatus.Draft]);
      expect(LEGAL_TRANSITIONS[ContentStatus.Draft]).toContain(ContentStatus.Review);
      expect(LEGAL_TRANSITIONS[ContentStatus.Draft]).toContain(ContentStatus.Approved);
      expect(LEGAL_TRANSITIONS[ContentStatus.Review]).toContain(ContentStatus.Approved);
      expect(LEGAL_TRANSITIONS[ContentStatus.Review]).toContain(ContentStatus.Draft);
      expect(LEGAL_TRANSITIONS[ContentStatus.Approved]).toContain(ContentStatus.Scheduled);
      expect(LEGAL_TRANSITIONS[ContentStatus.Approved]).toContain(ContentStatus.Published);
      expect(LEGAL_TRANSITIONS[ContentStatus.Scheduled]).toContain(ContentStatus.Approved);
      expect(LEGAL_TRANSITIONS[ContentStatus.Scheduled]).toContain(ContentStatus.Published);
      expect(LEGAL_TRANSITIONS[ContentStatus.Published]).toContain(ContentStatus.Approved);
    });
  });

  describe('nextStatus', () => {
    it('advances along the pipeline and stops at terminal states', () => {
      expect(nextStatus(ContentStatus.Idea)).toBe(ContentStatus.Draft);
      expect(nextStatus(ContentStatus.Draft)).toBe(ContentStatus.Review);
      expect(nextStatus(ContentStatus.Approved)).toBe(ContentStatus.Scheduled);
      expect(nextStatus(ContentStatus.Published)).toBeNull();
      expect(nextStatus(ContentStatus.Archived)).toBeNull();
    });
  });

  describe('relativeTime', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    it('formats past, future, and now', () => {
      const now = new Date('2026-06-02T12:00:00Z');
      jasmine.clock().mockDate(now);
      expect(relativeTime(new Date(now.getTime() - 2 * 3600_000).toISOString())).toBe('2h');
      expect(relativeTime(new Date(now.getTime() - 3 * 86400_000).toISOString())).toBe('3d');
      expect(relativeTime(new Date(now.getTime() + 3 * 86400_000).toISOString())).toBe('in 3d');
      expect(relativeTime(new Date(now.getTime() - 5_000).toISOString())).toBe('just now');
    });
  });
});
