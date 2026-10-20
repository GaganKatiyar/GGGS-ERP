import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface AssistantItem {
  readonly key: string;
  readonly title: string;
  readonly description: string;
  readonly tools: readonly string[];
}

export interface AiStatus {
  readonly configured: boolean;
  readonly model: string;
  readonly assistants: readonly AssistantItem[];
}

export interface ConversationItem {
  readonly id: string;
  readonly assistantKey: string;
  readonly title: string;
  readonly turns: number;
  readonly lastActivityAt: string;
}

export type AiActionStatus = 'Proposed' | 'Executed' | 'Rejected' | 'Failed' | 'Expired';

export interface ActionItem {
  readonly id: string;
  readonly toolName: string;
  readonly summary: string;
  readonly status: AiActionStatus;
  readonly proposedAt: string;
  readonly decidedAt: string | null;
  readonly result: string | null;
}

export interface ChatMessage {
  readonly role: 'user' | 'assistant';
  readonly text: string;
  readonly toolsUsed: readonly string[];
  readonly actionIds: readonly string[];
}

export interface ConversationView {
  readonly conversation: ConversationItem;
  readonly messages: readonly ChatMessage[];
  readonly actions: readonly ActionItem[];
}

export interface UsageRow {
  readonly assistantKey: string;
  readonly conversations: number;
  readonly turns: number;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly toolCalls: number;
  readonly actionsProposed: number;
  readonly actionsExecuted: number;
}

/** The assistants (`/api/v1/ai`). */
@Injectable({ providedIn: 'root' })
export class AssistantApi {
  private readonly http = inject(HttpClient);

  status(): Promise<AiStatus> {
    return firstValueFrom(this.http.get<AiStatus>('/api/v1/ai/status'));
  }

  conversations(): Promise<readonly ConversationItem[]> {
    return firstValueFrom(this.http.get<ConversationItem[]>('/api/v1/ai/conversations'));
  }

  conversation(id: string): Promise<ConversationView> {
    return firstValueFrom(this.http.get<ConversationView>(`/api/v1/ai/conversations/${id}`));
  }

  start(assistant: string, message: string): Promise<ConversationView> {
    return firstValueFrom(this.http.post<ConversationView>('/api/v1/ai/conversations', { assistant, message }));
  }

  send(id: string, message: string): Promise<ConversationView> {
    return firstValueFrom(this.http.post<ConversationView>(`/api/v1/ai/conversations/${id}/messages`, { message }));
  }

  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/ai/conversations/${id}`));
  }

  approve(actionId: string): Promise<ActionItem> {
    return firstValueFrom(this.http.post<ActionItem>(`/api/v1/ai/actions/${actionId}/approve`, null));
  }

  reject(actionId: string): Promise<ActionItem> {
    return firstValueFrom(this.http.post<ActionItem>(`/api/v1/ai/actions/${actionId}/reject`, null));
  }

  usage(days: number): Promise<readonly UsageRow[]> {
    return firstValueFrom(this.http.get<UsageRow[]>('/api/v1/ai/usage', { params: { days } }));
  }
}
