import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { parseMarkdown } from './markdown';

/**
 * Renders the Markdown subset from markdown.ts. Every piece of text goes through Angular interpolation,
 * so it is always escaped: the model's output cannot inject HTML, scripts or links.
 */
@Component({
  selector: 'app-markdown',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  template: `
    <ng-template #inline let-parts>
      @for (part of parts; track $index) {
        @switch (part.kind) {
          @case ('strong') {<strong>{{ part.text }}</strong>}
          @case ('em') {<em>{{ part.text }}</em>}
          @case ('code') {<code>{{ part.text }}</code>}
          @default {<span>{{ part.text }}</span>}
        }
      }
    </ng-template>

    @for (block of blocks(); track $index) {
      @switch (block.kind) {
        @case ('heading') {
          <p class="md-heading" [class.md-small]="block.level > 2">
            <ng-container *ngTemplateOutlet="inline; context: { $implicit: block.content }" />
          </p>
        }
        @case ('paragraph') {
          <p>
            @for (line of block.lines; track $index) {
              @if (!$first) {<br />}
              <ng-container *ngTemplateOutlet="inline; context: { $implicit: line }" />
            }
          </p>
        }
        @case ('list') {
          @if (block.ordered) {
            <ol [attr.start]="block.start === 1 ? null : block.start">
              @for (item of block.items; track $index) {
                <li><ng-container *ngTemplateOutlet="inline; context: { $implicit: item }" /></li>
              }
            </ol>
          } @else {
            <ul>
              @for (item of block.items; track $index) {
                <li><ng-container *ngTemplateOutlet="inline; context: { $implicit: item }" /></li>
              }
            </ul>
          }
        }
        @case ('table') {
          <div class="md-table">
            <table>
              <thead>
                <tr>
                  @for (cell of block.header; track $index; let c = $index) {
                    <th scope="col" [style.text-align]="block.align[c]">
                      <ng-container *ngTemplateOutlet="inline; context: { $implicit: cell }" />
                    </th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of block.rows; track $index) {
                  <tr>
                    @for (cell of row; track $index; let c = $index) {
                      <td [style.text-align]="block.align[c]">
                        <ng-container *ngTemplateOutlet="inline; context: { $implicit: cell }" />
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
        @case ('code') {
          <pre><code>{{ block.text }}</code></pre>
        }
        @case ('rule') {
          <hr />
        }
      }
    }
  `,
  styles: `
    :host { display: block; line-height: 1.55; overflow-wrap: anywhere; }
    p, ul, ol, pre, .md-table { margin: 0 0 8px; }
    :host > :last-child { margin-bottom: 0; }
    ul, ol { padding-left: 20px; }
    li + li { margin-top: 2px; }
    .md-heading { font-weight: 600; font-size: 1rem; margin-top: 4px; }
    .md-heading.md-small { font-size: 0.9375rem; }
    code { font-family: var(--font-mono); font-size: 0.8125rem; background: var(--color-surface-muted); border: 1px solid var(--color-border); border-radius: 4px; padding: 0 4px; }
    pre { background: var(--color-surface-muted); border: 1px solid var(--color-border); border-radius: var(--radius-sm); padding: 8px 10px; overflow-x: auto; }
    pre code { border: 0; padding: 0; background: none; }
    hr { border: 0; border-top: 1px solid var(--color-border); margin: 10px 0; }
    .md-table { overflow-x: auto; }
    table { border-collapse: collapse; font-size: 0.8125rem; font-variant-numeric: tabular-nums; }
    th, td { border: 1px solid var(--color-border); padding: 5px 8px; text-align: left; vertical-align: top; }
    th { background: var(--color-surface-muted); font-weight: 600; white-space: nowrap; }
  `,
})
export class MarkdownView {
  readonly text = input.required<string>();
  protected readonly blocks = computed(() => parseMarkdown(this.text()));
}
