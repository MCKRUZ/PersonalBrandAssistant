/** Greedy pack of sentences (hard-splitting over-long sentences by word) into chunks <= budget. */
function pack(text: string, budget: number): string[] {
  const sentences = text.match(/[^.!?]+[.!?]*\s*/g) ?? [text];
  const chunks: string[] = [];
  let cur = '';

  const flush = () => {
    if (cur.trim()) chunks.push(cur.trim());
    cur = '';
  };

  for (const sentence of sentences) {
    const s = sentence;
    if ((cur + s).trim().length <= budget) {
      cur += s;
      continue;
    }
    flush();
    if (s.trim().length <= budget) {
      cur = s;
      continue;
    }
    // sentence alone exceeds budget: hard-split by words
    let w = '';
    for (const word of s.trim().split(/\s+/)) {
      if (!w) {
        w = word;
      } else if ((w + ' ' + word).length <= budget) {
        w = w + ' ' + word;
      } else {
        chunks.push(w);
        w = word;
      }
    }
    cur = w ? w + ' ' : '';
  }
  flush();
  return chunks;
}

/**
 * Split plain text into tweets that, INCLUDING the " i/n" suffix, never exceed `limit` (default 280).
 * The suffix width depends on n, so the chunking re-packs until the chunk count is stable. A single
 * tweet that already fits the limit is returned unnumbered.
 */
export function splitThread(text: string, limit = 280): string[] {
  const trimmed = text.trim();
  if (!trimmed) return [];
  if (trimmed.length <= limit) return [trimmed];

  // Reserve worst-case suffix room (" n/n"); re-pack while the count (and thus suffix width) changes.
  let chunks = pack(trimmed, limit);
  let prevCount = -1;
  while (chunks.length !== prevCount) {
    prevCount = chunks.length;
    const reserve = ` ${chunks.length}/${chunks.length}`.length;
    chunks = pack(trimmed, limit - reserve);
  }

  const n = chunks.length;
  if (n === 1) return chunks; // collapsed back to a single tweet
  return chunks.map((c, i) => `${c} ${i + 1}/${n}`);
}
