import { toBlocks, plainText } from './markdown-blocks';

describe('markdown-blocks', () => {
  describe('toBlocks', () => {
    it('maps headings by depth and paragraphs to p, in document order', () => {
      const md = '# Title\n\nIntro paragraph.\n\n## Section\n\nMore text.';
      const blocks = toBlocks(md);
      expect(blocks).toEqual([
        { type: 'h1', text: 'Title' },
        { type: 'p', text: 'Intro paragraph.' },
        { type: 'h2', text: 'Section' },
        { type: 'p', text: 'More text.' },
      ]);
    });

    it('strips inline markers from block text', () => {
      const blocks = toBlocks('A **bold** and [label](https://x.com) word.');
      expect(blocks[0]).toEqual({ type: 'p', text: 'A bold and label word.' });
    });
  });

  describe('plainText', () => {
    it('drops emphasis/code markers and link hrefs but keeps the label', () => {
      expect(plainText('**bold**')).toBe('bold');
      expect(plainText('[label](https://example.com)')).toBe('label');
      expect(plainText('`code`')).toBe('code');
    });

    it('reports the rendered (visible) length, not the raw markdown length', () => {
      const md = '**' + 'x'.repeat(290) + '**'; // raw length 294, visible 290
      expect(plainText(md).length).toBe(290);
    });

    it('joins multiple blocks into a single visible string', () => {
      const text = plainText('# Heading\n\nFirst.\n\nSecond.');
      expect(text).toContain('Heading');
      expect(text).toContain('First.');
      expect(text).toContain('Second.');
    });
  });
});
