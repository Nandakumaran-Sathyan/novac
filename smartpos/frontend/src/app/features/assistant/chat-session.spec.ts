import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth/auth.service';
import { AIChatRequest } from '../../core/models';
import { assistantErrorMessage, ChatEntry, ChatSession, historyFor } from './chat-session';

const chatUrl = `${environment.apiUrl}/ai/chat`;
const reply = { reply: 'Today: **₹430.50**', toolsUsed: ['GetTodaySales'], truncated: false, durationMs: 12000 };

describe('historyFor', () => {
  it('sends only answered turns, as user/assistant text, newest twelve', () => {
    const entries: ChatEntry[] = [
      { id: 1, role: 'user', content: 'lost question', failed: true },
      { id: 2, role: 'assistant', content: 'Could not answer', error: true, retryText: 'lost question' },
      ...Array.from({ length: 14 }, (_, i): ChatEntry => ({ id: i + 3, role: i % 2 ? 'assistant' : 'user', content: `turn ${i}` })),
    ];
    const history = historyFor(entries);
    expect(history).toHaveLength(12);
    expect(history[0]).toEqual({ role: 'user', content: 'turn 2' });
    expect(history.some((t) => t.content.includes('lost'))).toBe(false);
    expect(Object.keys(history[0])).toEqual(['role', 'content']);
  });

  it('cuts long turns to the server limit', () => {
    expect(historyFor([{ id: 1, role: 'assistant', content: 'x'.repeat(5000) }])[0].content).toHaveLength(4000);
  });
});

describe('assistantErrorMessage', () => {
  it('uses the server text for a busy user, and explains a bare rate-limit rejection', () => {
    const busy = new HttpErrorResponse({ status: 429, error: { detail: 'Your previous question is still being answered.' } });
    expect(assistantErrorMessage(busy)).toBe('Your previous question is still being answered.');
    expect(assistantErrorMessage(new HttpErrorResponse({ status: 429 }))).toContain('too quickly');
  });

  it('shows the 503 detail, which the server writes for users', () => {
    const down = new HttpErrorResponse({ status: 503, error: { detail: 'The AI assistant is offline. Start Ollama and try again.' } });
    expect(assistantErrorMessage(down)).toBe('The AI assistant is offline. Start Ollama and try again.');
  });
});

describe('ChatSession', () => {
  let session: ChatSession;
  let http: HttpTestingController;

  beforeEach(() => {
    // A signed-in owner, as AuthService reads it from storage at start-up.
    const user = { id: 7, fullName: 'Owner', email: 'o@example.com', emailVerified: true, shopId: 3, shopName: 'Shop', createdAt: '', lastLoginAt: null };
    localStorage.setItem('smartpos.session', JSON.stringify({ accessToken: 't', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), user }));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '**', children: [] }])],
    });
    session = TestBed.inject(ChatSession);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  it('sends the question with the earlier turns and appends the answer', () => {
    session.send('  What are today sales?  ');
    const first = http.expectOne(chatUrl);
    expect(first.request.body).toEqual({ message: 'What are today sales?', history: [] } satisfies AIChatRequest);
    expect(session.pending()).toBe(true);
    first.flush(reply);

    expect(session.pending()).toBe(false);
    expect(session.entries().map((e) => e.role)).toEqual(['user', 'assistant']);
    expect(session.entries()[1]).toMatchObject({ content: reply.reply, toolsUsed: ['GetTodaySales'] });

    session.send('And yesterday?');
    const second = http.expectOne(chatUrl);
    expect((second.request.body as AIChatRequest).history).toEqual([
      { role: 'user', content: 'What are today sales?' },
      { role: 'assistant', content: reply.reply },
    ]);
    second.flush(reply);
    http.verify();
  });

  it('ignores empty input and a second question while one is running', () => {
    session.send('   ');
    session.send('first');
    session.send('second');
    expect(http.match(chatUrl)).toHaveLength(1);
  });

  it('shows a failure with a retry that replaces the failed attempt', () => {
    session.send('Top products?');
    http.expectOne(chatUrl).flush({ detail: 'The AI assistant is offline.' }, { status: 503, statusText: 'Unavailable' });
    http.match(`${environment.apiUrl}/ai/status`); // a 503 triggers a status re-check

    const notice = session.entries()[1];
    expect(notice).toMatchObject({ error: true, content: 'The AI assistant is offline.', retryText: 'Top products?' });
    expect(session.entries()[0].failed).toBe(true);

    session.retry(notice);
    expect(session.entries()).toHaveLength(1);
    http.expectOne(chatUrl).flush(reply);
    expect(session.entries().map((e) => e.error ?? false)).toEqual([false, false]);
  });

  it('stop cancels the request and offers a retry', () => {
    session.send('Slow question');
    const request = http.expectOne(chatUrl);
    session.stop();
    expect(request.cancelled).toBe(true);
    expect(session.pending()).toBe(false);
    expect(session.entries()[1]).toMatchObject({ error: true, retryText: 'Slow question' });
  });

  it('clears the conversation when the signed-in user changes', () => {
    session.send('Mine');
    http.expectOne(chatUrl).flush(reply);
    TestBed.inject(AuthService).logout();
    TestBed.tick();
    expect(session.entries()).toEqual([]);
  });
});
