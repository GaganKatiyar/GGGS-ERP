import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AnalyticsArea,
  CalendarScope,
  CalendarView,
  DashboardView,
  DigestFrequency,
  ImportHandlerInfo,
  ImportOutcome,
  ImportRunItem,
  IntegrationAdapter,
  IntegrationConnection,
  IntegrationConnectionInput,
  IntegrationRunItem,
  IntegrationRunOutcome,
  ReportSubscription,
  SearchResults,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Analytics dashboards and report digests (`/api/v1/analytics`). */
@Injectable({ providedIn: 'root' })
export class AnalyticsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/analytics';

  areas(): Promise<readonly AnalyticsArea[]> {
    return firstValueFrom(this.http.get<readonly AnalyticsArea[]>(this.base));
  }

  dashboard(area: string): Promise<DashboardView> {
    return firstValueFrom(this.http.get<DashboardView>(`${this.base}/${encodeURIComponent(area)}`));
  }

  subscriptions(): Promise<readonly ReportSubscription[]> {
    return firstValueFrom(this.http.get<readonly ReportSubscription[]>(`${this.base}/subscriptions`));
  }

  subscribe(area: string, frequency: DigestFrequency): Promise<readonly ReportSubscription[]> {
    return firstValueFrom(this.http.put<readonly ReportSubscription[]>(`${this.base}/subscriptions`, { area, frequency }));
  }

  unsubscribe(id: string): Promise<readonly ReportSubscription[]> {
    return firstValueFrom(this.http.delete<readonly ReportSubscription[]>(`${this.base}/subscriptions/${id}`));
  }
}

/** Global search (`/api/v1/search`) and the HR calendar (`/api/v1/calendar`). */
@Injectable({ providedIn: 'root' })
export class InsightApi {
  private readonly http = inject(HttpClient);

  search(term: string): Promise<SearchResults> {
    return firstValueFrom(this.http.get<SearchResults>('/api/v1/search', { params: { q: term } }));
  }

  calendar(from: string, to: string, scope: CalendarScope): Promise<CalendarView> {
    return firstValueFrom(this.http.get<CalendarView>('/api/v1/calendar', { params: { from, to, scope } }));
  }
}

/** The import framework (`/api/v1/imports`) and integrations (`/api/v1/integrations`). */
@Injectable({ providedIn: 'root' })
export class DataHubApi {
  private readonly http = inject(HttpClient);

  imports(): Promise<readonly ImportHandlerInfo[]> {
    return firstValueFrom(this.http.get<readonly ImportHandlerInfo[]>('/api/v1/imports'));
  }

  history(): Promise<readonly ImportRunItem[]> {
    return firstValueFrom(this.http.get<readonly ImportRunItem[]>('/api/v1/imports/history'));
  }

  template(key: string): Promise<string> {
    return firstValueFrom(this.http.get(`/api/v1/imports/${encodeURIComponent(key)}/template`, { responseType: 'text' }));
  }

  preview(key: string, csv: string, fileName: string | null): Promise<ImportOutcome> {
    return firstValueFrom(this.http.post<ImportOutcome>(`/api/v1/imports/${encodeURIComponent(key)}/preview`, { csv, fileName }));
  }

  run(key: string, csv: string, fileName: string | null): Promise<ImportOutcome> {
    return firstValueFrom(this.http.post<ImportOutcome>(`/api/v1/imports/${encodeURIComponent(key)}`, { csv, fileName }));
  }

  adapters(): Promise<readonly IntegrationAdapter[]> {
    return firstValueFrom(this.http.get<readonly IntegrationAdapter[]>('/api/v1/integrations/adapters'));
  }

  connections(): Promise<readonly IntegrationConnection[]> {
    return firstValueFrom(this.http.get<readonly IntegrationConnection[]>('/api/v1/integrations/connections'));
  }

  save(id: string | null, body: IntegrationConnectionInput): Promise<IntegrationConnection> {
    return firstValueFrom(
      id ? this.http.put<IntegrationConnection>(`/api/v1/integrations/connections/${id}`, body) : this.http.post<IntegrationConnection>('/api/v1/integrations/connections', body),
    );
  }

  test(id: string): Promise<IntegrationConnection> {
    return firstValueFrom(this.http.post<IntegrationConnection>(`/api/v1/integrations/connections/${id}/test`, {}));
  }

  runOperation(id: string, operation: string, parameters: Readonly<Record<string, string>>): Promise<IntegrationRunOutcome> {
    return firstValueFrom(this.http.post<IntegrationRunOutcome>(`/api/v1/integrations/connections/${id}/run`, { operation, parameters }));
  }

  runs(connectionId: string | null = null): Promise<readonly IntegrationRunItem[]> {
    return firstValueFrom(this.http.get<readonly IntegrationRunItem[]>('/api/v1/integrations/runs', { params: connectionId ? { connectionId } : {} }));
  }
}
