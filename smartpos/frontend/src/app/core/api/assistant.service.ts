import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AIChatRequest, AIChatResponse, AIStatus } from '../models';

/** The AI assistant API. The shop comes from the token on the server; the browser never sends it. */
@Injectable({ providedIn: 'root' })
export class AssistantService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/ai`;

  chat(request: AIChatRequest) {
    return this.http.post<AIChatResponse>(`${this.url}/chat`, request);
  }

  status() {
    return this.http.get<AIStatus>(`${this.url}/status`);
  }
}
