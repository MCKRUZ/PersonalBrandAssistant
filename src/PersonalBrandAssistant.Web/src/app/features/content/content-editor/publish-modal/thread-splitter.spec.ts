import { splitThread } from './thread-splitter';

describe('splitThread', () => {
  it('returns a single unnumbered tweet for short text', () => {
    expect(splitThread('Just a short thought.')).toEqual(['Just a short thought.']);
  });

  it('returns [] for empty input', () => {
    expect(splitThread('   ')).toEqual([]);
  });

  it('splits long text into numbered tweets, each <= limit including the suffix', () => {
    const sentence = 'This is a sentence with enough words to matter. ';
    const text = sentence.repeat(20); // well over 280
    const tweets = splitThread(text, 280);
    expect(tweets.length).toBeGreaterThan(1);
    for (const t of tweets) expect(t.length).toBeLessThanOrEqual(280);
    expect(tweets[0]).toMatch(/ 1\/\d+$/);
    expect(tweets[tweets.length - 1]).toMatch(new RegExp(` ${tweets.length}/${tweets.length}$`));
  });

  it('keeps every numbered tweet <= limit even with a multi-digit thread count', () => {
    // small limit forces many tweets so n becomes 2+ digits
    const text = Array.from({ length: 40 }, (_, i) => `Sentence number ${i} here.`).join(' ');
    const tweets = splitThread(text, 40);
    expect(tweets.length).toBeGreaterThanOrEqual(10);
    for (const t of tweets) expect(t.length).toBeLessThanOrEqual(40);
  });

  it('hard-splits a single over-long sentence by words', () => {
    const text = 'word '.repeat(100).trim(); // one long run, no sentence punctuation
    const tweets = splitThread(text, 50);
    for (const t of tweets) expect(t.length).toBeLessThanOrEqual(50);
  });
});
