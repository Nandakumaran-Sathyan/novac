import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { parseInline, parseMarkdown } from './markdown';
import { MarkdownView } from './markdown-view';

describe('parseInline', () => {
  it('splits bold, italic and code from plain text', () => {
    expect(parseInline('**Total sales**: ₹430.50 from *one* bill, see `GetTodaySales`')).toEqual([
      { kind: 'strong', text: 'Total sales' },
      { kind: 'text', text: ': ₹430.50 from ' },
      { kind: 'em', text: 'one' },
      { kind: 'text', text: ' bill, see ' },
      { kind: 'code', text: 'GetTodaySales' },
    ]);
  });

  it('leaves lone asterisks and snake_case alone', () => {
    expect(parseInline('2 * 3 = 6 and low_stock_level')).toEqual([{ kind: 'text', text: '2 * 3 = 6 and low_stock_level' }]);
  });
});

describe('parseMarkdown', () => {
  it('reads the bullet styles the model actually uses, flattening nested items', () => {
    const blocks = parseMarkdown('Low stock:\n\n*   **Curd 400 g** – out of stock\n- Burger Buns  \n  - nested item');
    expect(blocks.map((b) => b.kind)).toEqual(['paragraph', 'list']);
    const list = blocks[1];
    expect(list.kind === 'list' && list.items.length).toBe(3);
  });

  it('reads numbered lists and keeps their start number', () => {
    const [list] = parseMarkdown('3. Cola\n4. Juice');
    expect(list).toMatchObject({ kind: 'list', ordered: true, start: 3 });
  });

  it('reads a table with alignment and pads short rows', () => {
    const [table] = parseMarkdown('| Product | Units |\n|---------|------:|\n| Black Tea | 335 |\n| Cola |');
    expect(table.kind).toBe('table');
    if (table.kind === 'table') {
      expect(table.header.map((c) => c[0].text)).toEqual(['Product', 'Units']);
      expect(table.align).toEqual([null, 'right']);
      expect(table.rows[0][1]).toEqual([{ kind: 'text', text: '335' }]);
      expect(table.rows[1][1]).toEqual([]);
    }
  });

  it('right-aligns columns that hold only figures', () => {
    const [table] = parseMarkdown('| Rank | Product | Net Sales (INR) |\n|---|---|---|\n| 1 | Black Tea 250 g | 47,828.25 |\n| 2 | Cola | ₹14,268.00 |');
    expect(table.kind === 'table' && table.align).toEqual(['right', null, 'right']);
  });

  it('keeps line breaks inside a paragraph and separates blocks on blank lines', () => {
    const blocks = parseMarkdown('Line one\nLine two\n\n## Heading\n---\n```\nraw *text*\n```');
    expect(blocks.map((b) => b.kind)).toEqual(['paragraph', 'heading', 'rule', 'code']);
    expect(blocks[0].kind === 'paragraph' && blocks[0].lines.length).toBe(2);
    expect(blocks[3]).toEqual({ kind: 'code', text: 'raw *text*' });
  });
});

describe('MarkdownView', () => {
  it('renders HTML in the answer as text, never as markup', async () => {
    const fixture = TestBed.createComponent(MarkdownView);
    fixture.componentRef.setInput('text', '**Hi** <img src=x onerror="alert(1)"> <script>alert(2)</script>\n\n- [link](javascript:alert(3))');
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('strong')?.textContent).toBe('Hi');
    expect(el.querySelector('img, script, a')).toBeNull();
    expect(el.textContent).toContain('<script>alert(2)</script>');
  });

  it('renders tables and lists as real elements', async () => {
    const fixture = TestBed.createComponent(MarkdownView);
    fixture.componentRef.setInput('text', '| A | B |\n|---|---|\n| 1 | 2 |\n\n1. one\n2. two');
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('td').length).toBe(2);
    expect(el.querySelectorAll('ol li').length).toBe(2);
  });
});
