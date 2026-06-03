import { marked } from 'marked';

export type ProseBlock = { type: 'h1' | 'h2' | 'h3' | 'h4' | 'h5' | 'h6' | 'p'; text: string };

// marked's Token union is awkward to traverse with strict types; treat nodes loosely in this adapter.
type Tok = {
  type: string;
  text?: string;
  depth?: number;
  tokens?: Tok[];
  items?: Tok[];
};

/** Concatenate the VISIBLE text of a list of inline tokens, excluding markers and link hrefs. */
function inlineText(tokens: Tok[] | undefined): string {
  if (!tokens) return '';
  return tokens
    .map((t) => {
      if (t.type === 'text' || t.type === 'codespan' || t.type === 'escape') {
        return t.tokens ? inlineText(t.tokens) : t.text ?? '';
      }
      if (t.type === 'br') return '\n';
      if (t.tokens) return inlineText(t.tokens); // strong / em / del / link (label only)
      return t.text ?? '';
    })
    .join('');
}

function blockText(tok: Tok): string {
  return tok.tokens ? inlineText(tok.tokens) : tok.text ?? '';
}

/** Parse markdown into ordered prose blocks (headings by depth, paragraphs). */
export function toBlocks(markdown: string): ProseBlock[] {
  const tokens = marked.lexer(markdown) as unknown as Tok[];
  const blocks: ProseBlock[] = [];
  for (const tok of tokens) {
    if (tok.type === 'heading') {
      const depth = Math.min(6, Math.max(1, tok.depth ?? 1));
      blocks.push({ type: `h${depth}` as ProseBlock['type'], text: blockText(tok) });
    } else if (tok.type === 'paragraph') {
      blocks.push({ type: 'p', text: blockText(tok) });
    }
  }
  return blocks;
}

/**
 * Strip markdown to plain text reflecting RENDERED (visible) length: link labels kept, hrefs and
 * emphasis/code markers dropped. Used for LinkedIn (3000) / Twitter (280) budgets and thread splits.
 */
export function plainText(markdown: string): string {
  const tokens = marked.lexer(markdown) as unknown as Tok[];
  const parts: string[] = [];
  const visit = (toks: Tok[]): void => {
    for (const tok of toks) {
      switch (tok.type) {
        case 'heading':
        case 'paragraph':
          parts.push(blockText(tok));
          break;
        case 'text':
          parts.push(blockText(tok));
          break;
        case 'code':
          parts.push(tok.text ?? '');
          break;
        case 'blockquote':
          if (tok.tokens) visit(tok.tokens);
          break;
        case 'list':
          for (const item of tok.items ?? []) parts.push(blockText(item));
          break;
        default:
          if (tok.tokens) visit(tok.tokens);
      }
    }
  };
  visit(tokens);
  return parts.join(' ').replace(/\s+/g, ' ').trim();
}
