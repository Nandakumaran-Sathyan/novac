import { HttpErrorResponse } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { AssistantService } from '../../core/api/assistant.service';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/http-utils';
import { AIChatTurn, AIStatus } from '../../core/models';

export interface ChatEntry {
  id: number;
  role: 'user' | 'assistant';
  content: string;
  toolsUsed?: string[];
  truncated?: boolean;
  durationMs?: number;
  /** An assistant-side failure notice. Not part of the history sent back to the server. */
  error?: boolean;
  /** A user question that got no answer (failed or stopped). Also left out of the history. */
  failed?: boolean;
  /** On an error notice: the question to send again. */
  retryText?: string;
}

/** Limits the server enforces on the request (AIChatRequest). It trims history further on its side. */
export const MAX_MESSAGE_LENGTH = 1000;
const MAX_HISTORY_TURNS = 12;
const MAX_TURN_LENGTH = 4000;

/** Friendly names for the read-only data tools, shown under an answer as "Data used". */
const TOOL_LABELS: Record<string, string> = {
  GetTodaySales: "Today's sales",
  GetYesterdaySales: "Yesterday's sales",
  GetTopSellingProducts: 'Top sellers',
  GetMostProfitableProducts: 'Profit by product',
  GetLowStockProducts: 'Low stock',
  GetExpiringProducts: 'Expiry dates',
  GetInventoryValue: 'Inventory value',
  GetUnsoldProducts: 'Unsold products',
};

export const toolLabel = (name: string): string => TOOL_LABELS[name] ?? name;

/** The earlier turns to send with a new question: answered questions and their answers, newest last. */
export function historyFor(entries: ChatEntry[]): AIChatTurn[] {
  return entries
    .filter((e) => !e.error && !e.failed)
    .slice(-MAX_HISTORY_TURNS)
    .map((e) => ({ role: e.role, content: e.content.slice(0, MAX_TURN_LENGTH) }));
}

/** Turns an API error into what the assistant should say. 429 and 503 carry user-safe text from the server. */
export function assistantErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.status === 429 && typeof error.error?.detail !== 'string') {
    // The rate limiter rejects without a body.
    return "You're asking questions too quickly. Wait a minute and try again.";
  }
  if (error instanceof HttpErrorResponse && error.status === 503 && typeof error.error?.detail !== 'string') {
    return 'The AI assistant is unavailable right now. Please try again later.';
  }
  return errorMessage(error, 'The assistant could not answer. Please try again.');
}

/**
 * The conversation with the assistant, kept in memory for this browser tab only (a reload starts afresh).
 * Shared by the dashboard panel and the Assistant page, and cleared whenever the signed-in user changes,
 * so one owner never sees another's questions.
 */
@Injectable({ providedIn: 'root' })
export class ChatSession {
  private readonly api = inject(AssistantService);
  private readonly auth = inject(AuthService);
  private readonly state = signal<ChatEntry[]>([]);
  private readonly statusState = signal<AIStatus | null>(null);
  private request?: Subscription;
  private nextId = 1;

  readonly entries = this.state.asReadonly();
  readonly status = this.statusState.asReadonly();
  readonly pending = signal(false);
  /** When the current question was sent (ms since epoch), for the elapsed-time counter. */
  readonly pendingSince = signal<number | null>(null);
  readonly checkingStatus = signal(false);
  /** Why the assistant cannot answer, if the last status check said so. */
  readonly statusProblem = computed(() => {
    const s = this.statusState();
    if (!s || s.ready) {
      return null;
    }
    return s.problem ?? 'The AI assistant is not ready.';
  });

  constructor() {
    effect(() => {
      this.auth.user()?.id;
      untracked(() => this.clear());
    });
  }

  send(text: string): void {
    const message = text.trim().slice(0, MAX_MESSAGE_LENGTH);
    if (!message || this.pending()) {
      return;
    }
    const history = historyFor(this.state());
    const question: ChatEntry = { id: this.nextId++, role: 'user', content: message };
    this.state.update((list) => [...list, question]);
    this.pending.set(true);
    this.pendingSince.set(Date.now());

    this.request = this.api.chat({ message, history }).subscribe({
      next: (response) => {
        this.finish();
        this.append({
          role: 'assistant',
          content: response.reply,
          toolsUsed: response.toolsUsed,
          truncated: response.truncated,
          durationMs: response.durationMs,
        });
      },
      error: (err: unknown) => {
        this.finish();
        this.fail(question.id, assistantErrorMessage(err));
        if (err instanceof HttpErrorResponse && err.status === 503) {
          this.checkStatus();
        }
      },
    });
  }

  /** Abandons the question in flight. The server notices the closed request and stops the model. */
  stop(): void {
    const question = this.pending() ? [...this.state()].reverse().find((e) => e.role === 'user') : undefined;
    this.request?.unsubscribe();
    this.finish();
    if (question) {
      this.fail(question.id, 'You stopped this question.');
    }
  }

  /** Sends a failed question again, in place of the failed attempt. */
  retry(notice: ChatEntry): void {
    if (!notice.retryText || this.pending()) {
      return;
    }
    const index = this.state().indexOf(notice);
    // Drop the notice and the unanswered question just before it.
    this.state.update((list) => list.filter((_, i) => i !== index && i !== index - 1));
    this.send(notice.retryText);
  }

  clear(): void {
    this.request?.unsubscribe();
    this.finish();
    this.state.set([]);
  }

  checkStatus(): void {
    this.checkingStatus.set(true);
    this.api.status().subscribe({
      next: (s) => {
        this.statusState.set(s);
        this.checkingStatus.set(false);
      },
      error: () => {
        // The status call itself failed (API down, signed out): leave it to the chat request to explain.
        this.checkingStatus.set(false);
      },
    });
  }

  private finish(): void {
    this.request = undefined;
    this.pending.set(false);
    this.pendingSince.set(null);
  }

  private fail(questionId: number, message: string): void {
    const question = this.state().find((e) => e.id === questionId);
    this.state.update((list) => list.map((e) => (e.id === questionId ? { ...e, failed: true } : e)));
    this.append({ role: 'assistant', content: message, error: true, retryText: question?.content });
  }

  private append(entry: Omit<ChatEntry, 'id'>): void {
    this.state.update((list) => [...list, { id: this.nextId++, ...entry }]);
  }
}
