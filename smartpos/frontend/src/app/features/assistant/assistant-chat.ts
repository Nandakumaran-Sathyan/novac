import { ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, input, OnInit, signal, viewChild, afterRenderEffect } from '@angular/core';
import { SettingsService } from '../../core/api/settings.service';
import { Icon } from '../../shared/components/icon';
import { MarkdownView } from '../../shared/markdown/markdown-view';
import { ChatSession, MAX_MESSAGE_LENGTH, toolLabel } from './chat-session';

interface Suggestion {
  label: string;
  question: string;
}

const SUGGESTIONS: Suggestion[] = [
  { label: "Today's sales?", question: 'What are my total sales today?' },
  { label: 'Top-selling products?', question: 'Which are my top-selling products this month?' },
  { label: 'Low-stock products?', question: 'Which products are running low on stock?' },
  { label: 'Products nearing expiry?', question: 'Which products are nearing expiry?' },
];
const MORE_SUGGESTIONS: Suggestion[] = [
  { label: 'Most profitable products?', question: 'What are my most profitable products in the last 30 days?' },
  { label: 'Inventory value?', question: 'What is my current inventory value?' },
  { label: 'Unsold products?', question: "Which products haven't sold in the last 30 days?" },
  { label: 'Yesterday vs today?', question: "How do today's sales compare with yesterday's?" },
];

/** The chat itself: conversation, suggested questions and the input box. Used on the dashboard and the Assistant page. */
@Component({
  selector: 'app-assistant-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, MarkdownView],
  template: `
    @if (session.statusProblem(); as problem) {
      <div class="alert alert-info status" role="status">
        <app-icon name="alert" [size]="16" />
        <span class="grow">{{ problem }}</span>
        <button type="button" class="btn btn-secondary btn-sm" (click)="session.checkStatus()" [disabled]="session.checkingStatus()">
          {{ session.checkingStatus() ? 'Checking…' : 'Check again' }}
        </button>
      </div>
    }

    <div class="log" #log role="log" aria-live="polite" aria-label="Conversation with the assistant">
      @if (session.entries().length === 0) {
        <div class="intro">
          <span class="intro-icon"><app-icon name="sparkles" [size]="20" /></span>
          <p class="intro-title">Ask about {{ settings.storeName() }}</p>
          <p class="muted">
            Answers come from your shop's live sales and stock data. The assistant runs on this computer,
            so an answer can take up to a minute.
          </p>
          <div class="suggestions">
            @for (s of suggestions(); track s.label) {
              <button type="button" class="chip" (click)="ask(s.question)" [disabled]="session.pending()">{{ s.label }}</button>
            }
          </div>
        </div>
      }

      @for (entry of session.entries(); track entry.id) {
        @if (entry.role === 'user') {
          <div class="msg user" [class.failed]="entry.failed">
            <div class="bubble">{{ entry.content }}</div>
          </div>
        } @else {
          <div class="msg assistant" [class.error]="entry.error">
            <span class="avatar" aria-hidden="true"><app-icon [name]="entry.error ? 'alert' : 'sparkles'" [size]="14" /></span>
            <div class="bubble">
              <span class="sr-only">Assistant:</span>
              @if (entry.error) {
                <p>{{ entry.content }}</p>
                @if (entry.retryText) {
                  <button type="button" class="btn btn-secondary btn-sm retry" (click)="session.retry(entry)" [disabled]="session.pending()">
                    <app-icon name="refresh" [size]="14" /> Try again
                  </button>
                }
              } @else {
                <app-markdown [text]="entry.content" />
                @if (entry.truncated) {
                  <p class="note">This answer was cut short. Ask a narrower question for the rest.</p>
                }
                <div class="meta">
                  @if (entry.toolsUsed?.length) {
                    <span class="meta-label">Data used:</span>
                    @for (tool of entry.toolsUsed; track tool) {
                      <span class="tool">{{ label(tool) }}</span>
                    }
                  } @else {
                    <span class="meta-label">No shop data used</span>
                  }
                  @if (entry.durationMs !== undefined) {
                    <span class="time">{{ seconds(entry.durationMs) }} s</span>
                  }
                </div>
              }
            </div>
          </div>
        }
      }

      @if (session.pending()) {
        <div class="msg assistant thinking" role="status">
          <span class="avatar" aria-hidden="true"><app-icon name="sparkles" [size]="14" /></span>
          <div class="bubble">
            <span class="dots" aria-hidden="true"><i></i><i></i><i></i></span>
            <span>{{ thinkingText() }}</span>
            <button type="button" class="btn btn-ghost btn-sm stop" (click)="session.stop()">Stop</button>
          </div>
        </div>
      }
    </div>

    <form class="composer" (submit)="submit($event)">
      <label class="sr-only" for="assistant-input-{{ uid }}">Your question</label>
      <textarea
        #box
        id="assistant-input-{{ uid }}"
        class="textarea"
        rows="1"
        [attr.maxlength]="maxLength"
        placeholder="Ask about sales, stock or expiry…"
        [value]="draft()"
        (input)="onInput($event)"
        (keydown)="onKeydown($event)"
      ></textarea>
      <button type="submit" class="btn btn-primary btn-icon" [disabled]="!canSend()" aria-label="Send question">
        <app-icon name="send" [size]="16" />
      </button>
    </form>
    <div class="footer">
      <span>Read-only: the assistant can look at your shop's data but never change it.</span>
      @if (draft().length > maxLength - 100) {
        <span class="count">{{ draft().length }} / {{ maxLength }}</span>
      }
      @if (session.entries().length > 0 && !compact()) {
        <button type="button" class="btn btn-ghost btn-sm" (click)="session.clear()">New conversation</button>
      }
    </div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 0; }
    .status { margin: 0 0 10px; align-items: center; }
    .log { flex: 1; min-height: 0; overflow-y: auto; display: flex; flex-direction: column; gap: 12px; padding: 4px 2px 12px; }
    :host(.compact) .log { max-height: 420px; }
    .intro { display: flex; flex-direction: column; align-items: center; text-align: center; gap: 6px; padding: 20px 8px; margin: auto 0; }
    .intro-icon { display: inline-flex; padding: 10px; border-radius: 999px; background: var(--color-primary-soft); color: var(--color-primary-text); }
    .intro-title { font-weight: 600; }
    .intro .muted { max-width: 460px; font-size: 0.875rem; }
    .suggestions { display: flex; flex-wrap: wrap; justify-content: center; gap: 8px; margin-top: 8px; }
    .chip { border: 1px solid var(--color-border-strong); background: var(--color-surface); color: var(--color-text); border-radius: 999px; padding: 6px 12px; font: inherit; font-size: 0.8125rem; cursor: pointer; }
    .chip:hover:not(:disabled) { border-color: var(--color-primary); color: var(--color-primary-text); background: var(--color-primary-soft); }
    .chip:disabled { opacity: 0.6; cursor: default; }
    .msg { display: flex; gap: 8px; align-items: flex-start; max-width: 100%; }
    .msg.user { justify-content: flex-end; }
    .bubble { padding: 9px 13px; border-radius: var(--radius-lg); max-width: min(680px, 88%); font-size: 0.9375rem; min-width: 0; }
    .user .bubble { background: var(--color-primary); color: var(--color-text-inverse); border-bottom-right-radius: 4px; white-space: pre-wrap; overflow-wrap: anywhere; }
    .user.failed .bubble { opacity: 0.6; }
    .assistant .bubble { background: var(--color-surface-muted); border: 1px solid var(--color-border); border-bottom-left-radius: 4px; }
    .assistant.error .bubble { background: var(--color-danger-soft); color: var(--color-danger-text); border-color: transparent; }
    .avatar { flex: none; display: inline-grid; place-items: center; width: 26px; height: 26px; border-radius: 999px; background: var(--color-primary-soft); color: var(--color-primary-text); margin-top: 2px; }
    .error .avatar { background: var(--color-danger-soft); color: var(--color-danger-text); }
    .retry { margin-top: 8px; }
    .note { margin: 8px 0 0; font-size: 0.8125rem; color: var(--color-warning-text); }
    .meta { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 6px; margin-top: 8px; padding-top: 7px; border-top: 1px solid var(--color-border); font-size: 0.75rem; color: var(--color-text-muted); }
    .tool { background: var(--color-info-soft); color: var(--color-info-text); border-radius: 999px; padding: 1px 8px; }
    .time { margin-left: auto; font-variant-numeric: tabular-nums; }
    .thinking .bubble { display: flex; align-items: center; gap: 10px; color: var(--color-text-muted); font-size: 0.875rem; }
    .stop { margin-left: 4px; }
    .dots { display: inline-flex; gap: 4px; }
    .dots i { width: 6px; height: 6px; border-radius: 50%; background: var(--color-primary); animation: blink 1.2s infinite ease-in-out both; }
    .dots i:nth-child(2) { animation-delay: 0.15s; }
    .dots i:nth-child(3) { animation-delay: 0.3s; }
    @keyframes blink { 0%, 80%, 100% { opacity: 0.25; transform: scale(0.8); } 40% { opacity: 1; transform: none; } }
    @media (prefers-reduced-motion: reduce) { .dots i { animation: none; opacity: 0.7; } }
    .composer { display: flex; gap: 8px; align-items: flex-end; padding-top: 10px; border-top: 1px solid var(--color-border); }
    .composer .textarea { flex: 1; min-height: 40px; max-height: 160px; resize: none; line-height: 1.45; }
    .composer .btn-icon { height: 40px; width: 40px; }
    .footer { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 12px; margin-top: 6px; font-size: 0.75rem; color: var(--color-text-muted); }
    .footer > span:first-child { flex: 1; }
    .count { font-variant-numeric: tabular-nums; }
    @media (max-width: 640px) { .bubble { max-width: 92%; } .avatar { display: none; } }
  `,
  host: { '[class.compact]': 'compact()' },
})
export class AssistantChat implements OnInit {
  private static nextUid = 1;

  protected readonly session = inject(ChatSession);
  protected readonly settings = inject(SettingsService);

  /** Dashboard mode: fewer suggestions and a fixed-height conversation. */
  readonly compact = input(false);

  protected readonly uid = AssistantChat.nextUid++;
  protected readonly maxLength = MAX_MESSAGE_LENGTH;
  protected readonly draft = signal('');
  protected readonly label = toolLabel;
  protected readonly suggestions = computed(() => (this.compact() ? SUGGESTIONS : [...SUGGESTIONS, ...MORE_SUGGESTIONS]));
  protected readonly canSend = computed(() => this.draft().trim().length > 0 && !this.session.pending());

  private readonly log = viewChild.required<ElementRef<HTMLElement>>('log');
  private readonly box = viewChild.required<ElementRef<HTMLTextAreaElement>>('box');
  private readonly now = signal(Date.now());

  protected readonly thinkingText = computed(() => {
    const since = this.session.pendingSince();
    const elapsed = since ? Math.max(0, Math.floor((this.now() - since) / 1000)) : 0;
    if (elapsed < 5) {
      return 'Thinking…';
    }
    return elapsed < 20 ? `Looking at your data… ${elapsed} s` : `Still working… ${elapsed} s (answers can take up to a minute)`;
  });

  constructor() {
    // Tick once a second, only while a question is running.
    effect((onCleanup) => {
      if (!this.session.pending()) {
        return;
      }
      const timer = setInterval(() => this.now.set(Date.now()), 1000);
      onCleanup(() => clearInterval(timer));
    });
    // Keep the newest message in view.
    afterRenderEffect(() => {
      this.session.entries();
      this.session.pending();
      const el = this.log().nativeElement;
      el.scrollTop = el.scrollHeight;
    });
  }

  ngOnInit(): void {
    if (!this.session.status()) {
      this.session.checkStatus();
    }
  }

  protected ask(question: string): void {
    this.session.send(question);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.canSend()) {
      return;
    }
    this.session.send(this.draft());
    this.draft.set('');
    this.resize();
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
    this.resize();
  }

  /** Enter sends; Shift+Enter adds a new line (and nothing fires while an input method is composing). */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      this.submit(event);
    }
  }

  protected seconds(ms: number): string {
    return (ms / 1000).toFixed(ms < 10_000 ? 1 : 0);
  }

  /** Grows the box with its content, up to the CSS max-height. */
  private resize(): void {
    const el = this.box().nativeElement;
    el.style.height = 'auto';
    el.style.height = `${el.scrollHeight + 2}px`;
  }
}
