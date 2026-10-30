// A small Markdown subset for the assistant's answers: paragraphs, headings, bullet and numbered lists,
// tables, code, bold and italic. It parses to plain data that templates render as text, so nothing in the
// model's output can ever become HTML (no innerHTML, no links, no images).

export type InlineKind = 'text' | 'strong' | 'em' | 'code';
export interface Inline {
  kind: InlineKind;
  text: string;
}
export type Align = 'left' | 'center' | 'right' | null;

export type Block =
  | { kind: 'paragraph'; lines: Inline[][] }
  | { kind: 'heading'; level: number; content: Inline[] }
  | { kind: 'list'; ordered: boolean; start: number; items: Inline[][] }
  | { kind: 'table'; header: Inline[][]; align: Align[]; rows: Inline[][][] }
  | { kind: 'code'; text: string }
  | { kind: 'rule' };

const FENCE = /^\s*```/;
const HEADING = /^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$/;
const BULLET = /^\s*[-*+•]\s+(.*)$/;
const NUMBERED = /^\s*(\d{1,9})[.)]\s+(.*)$/;
const RULE = /^\s{0,3}([-*_])(\s*\1){2,}\s*$/;
const TABLE_SEPARATOR = /^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$/;

export function parseMarkdown(source: string): Block[] {
  const lines = source.replace(/\r\n?/g, '\n').split('\n');
  const blocks: Block[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

    if (line.trim() === '') {
      i++;
      continue;
    }

    if (FENCE.test(line)) {
      const code: string[] = [];
      i++;
      while (i < lines.length && !FENCE.test(lines[i])) {
        code.push(lines[i++]);
      }
      i++; // closing fence (or end of text)
      blocks.push({ kind: 'code', text: code.join('\n') });
      continue;
    }

    const heading = HEADING.exec(line);
    if (heading) {
      blocks.push({ kind: 'heading', level: heading[1].length, content: parseInline(heading[2]) });
      i++;
      continue;
    }

    if (RULE.test(line)) {
      blocks.push({ kind: 'rule' });
      i++;
      continue;
    }

    if (line.includes('|') && i + 1 < lines.length && TABLE_SEPARATOR.test(lines[i + 1]) && lines[i + 1].includes('-')) {
      const header = splitRow(line);
      const align = splitRow(lines[i + 1]).map(alignment);
      const rows: Inline[][][] = [];
      i += 2;
      while (i < lines.length && lines[i].includes('|') && lines[i].trim() !== '') {
        const cells = splitRow(lines[i++]);
        rows.push(header.map((_, c) => parseInline(cells[c] ?? '')));
      }
      blocks.push({
        kind: 'table',
        header: header.map(parseInline),
        align: header.map((_, c) => align[c] ?? (isNumericColumn(rows, c) ? 'right' : null)),
        rows,
      });
      continue;
    }

    const bullet = BULLET.exec(line);
    const numbered = NUMBERED.exec(line);
    if (bullet || numbered) {
      // Nested items are flattened into the same list: answers rarely nest, and this keeps it readable.
      const ordered = !bullet;
      const items: Inline[][] = [];
      const start = numbered ? Number(numbered[1]) : 1;
      while (i < lines.length) {
        const item = ordered ? NUMBERED.exec(lines[i]) : BULLET.exec(lines[i]);
        if (item) {
          items.push(parseInline(ordered ? item[2] : item[1]));
        } else if (/^\s+\S/.test(lines[i]) && items.length > 0 && !BULLET.test(lines[i]) && !NUMBERED.test(lines[i])) {
          // An indented continuation line belongs to the previous item.
          items[items.length - 1].push({ kind: 'text', text: ' ' }, ...parseInline(lines[i].trim()));
        } else {
          break;
        }
        i++;
      }
      blocks.push({ kind: 'list', ordered, start, items: items.map(mergeText) });
      continue;
    }

    // Paragraph: consecutive lines until a blank line or another block starts. Line breaks are kept,
    // as people expect in a chat.
    const paragraph: Inline[][] = [];
    while (i < lines.length && lines[i].trim() !== '' && !startsBlock(lines, i)) {
      paragraph.push(parseInline(lines[i].trim()));
      i++;
    }
    blocks.push({ kind: 'paragraph', lines: paragraph });
  }

  return blocks;
}

function startsBlock(lines: string[], i: number): boolean {
  const line = lines[i];
  return (
    FENCE.test(line) ||
    HEADING.test(line) ||
    RULE.test(line) ||
    BULLET.test(line) ||
    NUMBERED.test(line) ||
    (line.includes('|') && i + 1 < lines.length && TABLE_SEPARATOR.test(lines[i + 1]) && lines[i + 1].includes('-'))
  );
}

function splitRow(line: string): string[] {
  let row = line.trim();
  if (row.startsWith('|')) {
    row = row.slice(1);
  }
  if (row.endsWith('|')) {
    row = row.slice(0, -1);
  }
  return row.split('|').map((cell) => cell.trim());
}

// Plain figures such as "₹1,234.50", "-12" or "45%": right-aligned, like the app's own tables.
const NUMERIC_CELL = /^[^\d\s-]{0,3}\s?-?[\d,]+(\.\d+)?\s?%?$/;

function isNumericColumn(rows: Inline[][][], column: number): boolean {
  const cells = rows.map((row) => row[column].map((part) => part.text).join('').trim()).filter((text) => text !== '');
  return cells.length > 0 && cells.every((text) => NUMERIC_CELL.test(text));
}

function alignment(cell: string): Align {
  const left = cell.startsWith(':');
  const right = cell.endsWith(':');
  return left && right ? 'center' : right ? 'right' : left ? 'left' : null;
}

// `code`, **bold** / __bold__, *italic*. Underscore italics are left out so snake_case words stay intact.
const INLINE = /`([^`\n]+)`|\*\*(.+?)\*\*|__(.+?)__|\*(?!\s)([^*\n]+?)\*/g;

export function parseInline(text: string): Inline[] {
  const parts: Inline[] = [];
  let last = 0;
  for (const match of text.matchAll(INLINE)) {
    if (match.index > last) {
      parts.push({ kind: 'text', text: text.slice(last, match.index) });
    }
    if (match[1] !== undefined) {
      parts.push({ kind: 'code', text: match[1] });
    } else if (match[2] !== undefined || match[3] !== undefined) {
      parts.push({ kind: 'strong', text: match[2] ?? match[3] });
    } else {
      parts.push({ kind: 'em', text: match[4] });
    }
    last = match.index + match[0].length;
  }
  if (last < text.length) {
    parts.push({ kind: 'text', text: text.slice(last) });
  }
  return mergeText(parts);
}

function mergeText(parts: Inline[]): Inline[] {
  const merged: Inline[] = [];
  for (const part of parts) {
    const previous = merged[merged.length - 1];
    if (previous?.kind === 'text' && part.kind === 'text') {
      previous.text += part.text;
    } else {
      merged.push({ ...part });
    }
  }
  return merged;
}
