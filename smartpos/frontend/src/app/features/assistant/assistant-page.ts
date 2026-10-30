import { ChangeDetectionStrategy, Component } from '@angular/core';
import { AssistantChat } from './assistant-chat';

/** Full-page view of the same conversation shown on the dashboard. */
@Component({
  selector: 'app-assistant-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AssistantChat],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Assistant</h1>
          <p>Ask questions about your sales, stock and expiry dates in plain language.</p>
        </div>
      </div>
      <section class="card chat-card">
        <app-assistant-chat />
      </section>
    </div>
  `,
  styles: `
    .chat-card { display: flex; flex-direction: column; padding: 16px 18px 12px; height: calc(100dvh - var(--header-height) - 150px); min-height: 440px; }
    .chat-card app-assistant-chat { flex: 1; min-height: 0; }
    @media (max-width: 640px) { .chat-card { padding: 12px; height: calc(100dvh - var(--header-height) - 110px); min-height: 380px; } }
  `,
})
export class AssistantPage {}
