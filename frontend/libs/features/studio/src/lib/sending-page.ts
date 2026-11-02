import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { DataHubApi } from '@gggs/shared/insight';
import type { ChannelSendOutcome, MessageChannel } from '@gggs/shared/models';
import { PageHeader, Skeleton } from '@gggs/shared/ui';
import { CHANNELS, type ChannelCopy } from './studio-model';

interface Card extends ChannelCopy {
  readonly channel: MessageChannel;
}

/** Which of the organisation's connections sends its email and text messages. */
@Component({
  selector: 'gggs-sending-page',
  imports: [RouterLink, PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Sending" subtitle="Send Nuts and Bolts email from your own domain, and reach people by text message." />

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else {
      <div class="sending">
        @for (card of cards(); track card.channel.channel) {
          <section class="sending__card elevated" [attr.aria-labelledby]="'heading-' + card.channel.channel">
            <span class="sending__tile" aria-hidden="true"><i [class]="card.icon"></i></span>
            <h2 [id]="'heading-' + card.channel.channel">{{ card.title }}</h2>
            <p class="muted">{{ card.description }}</p>

            <label class="field">
              <span class="field__label">Send through</span>
              <select [disabled]="!canManage || busy() === card.channel.channel" (change)="choose(card.channel.channel, $any($event.target).value)">
                <option value="" [selected]="!card.channel.connectionId">{{ card.none }}</option>
                @for (option of card.channel.connections; track option.id) {
                  <option [value]="option.id" [selected]="option.id === card.channel.connectionId">{{ option.name }} ({{ option.adapterName }}){{ option.isActive ? '' : ' - switched off' }}</option>
                }
              </select>
            </label>

            @if (!card.channel.connections.length) {
              <p class="muted sending__note">Nothing that can do this is connected yet. <a routerLink="../connectors">Open Connectors</a></p>
            }
            @if (switchedOff(card.channel)) {
              <p class="alert alert--warning sending__note"><i class="pi pi-info-circle" aria-hidden="true"></i><span>This connection is switched off, so {{ card.fallback }}</span></p>
            }

            @if (canManage && card.channel.connectionId && !switchedOff(card.channel)) {
              <form class="sending__test" (submit)="$event.preventDefault(); test(card.channel.channel)">
                <label class="field">
                  <span class="field__label">{{ card.testLabel }}</span>
                  <input [type]="card.inputType" [placeholder]="card.placeholder" [value]="testTo()[card.channel.channel] || ''" (input)="setTestTo(card.channel.channel, $any($event.target).value)" />
                </label>
                <button type="submit" class="btn btn--sm" [disabled]="testing() === card.channel.channel || !testTo()[card.channel.channel]?.trim()">
                  <i class="pi pi-send" aria-hidden="true"></i> {{ testing() === card.channel.channel ? 'Sending…' : 'Send a test' }}
                </button>
              </form>
              @if (results()[card.channel.channel]; as outcome) {
                <p class="alert" [class.alert--success]="outcome.succeeded" [class.alert--danger]="!outcome.succeeded" role="status">{{ outcome.succeeded ? 'Sent. Check that it arrived.' : outcome.message }}</p>
              }
            }

            @if (card.channel.channel === 'sms') {
              <p class="muted sending__note">Choose which notifications go by text under <a routerLink="/admin/notifications">Notifications</a>. None do until you switch them on. They go to the mobile number on each person's record, and people can opt out in My preferences.</p>
            }
          </section>
        }
      </div>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .sending { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(24rem, 100%), 1fr)); gap: var(--gggs-space-4); align-items: start; }
    .sending__card { display: flex; flex-direction: column; gap: var(--gggs-space-3); padding: var(--gggs-space-5); }
    .sending__card h2 { margin: 0; font-size: var(--gggs-font-size-lg); }
    .sending__card p { margin: 0; }
    .sending__tile { display: grid; place-items: center; width: 2.5rem; height: 2.5rem; border-radius: var(--gggs-radius-md); background: var(--gggs-gradient-tile); color: var(--gggs-color-primary); }
    .sending__note { font-size: var(--gggs-font-size-sm); }
    .sending__test { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--gggs-space-3); }
    .sending__test .field { flex: 1 1 14rem; min-width: 0; }
  `,
})
export class SendingPage {
  private readonly api = inject(DataHubApi);

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly loading = signal(true);
  protected readonly channels = signal<readonly MessageChannel[]>([]);
  protected readonly busy = signal<string | null>(null);
  protected readonly testing = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly testTo = signal<Readonly<Record<string, string>>>({});
  protected readonly results = signal<Readonly<Record<string, ChannelSendOutcome | undefined>>>({});

  protected readonly cards = computed<readonly Card[]>(() => this.channels().flatMap((channel) => (CHANNELS[channel.channel] ? [{ ...CHANNELS[channel.channel], channel }] : [])));

  constructor() {
    void this.load();
  }

  protected switchedOff(channel: MessageChannel): boolean {
    return !!channel.connectionId && channel.connections.find((option) => option.id === channel.connectionId)?.isActive === false;
  }

  protected setTestTo(channel: string, value: string): void {
    this.testTo.update((values) => ({ ...values, [channel]: value }));
  }

  protected async choose(channel: string, connectionId: string): Promise<void> {
    this.busy.set(channel);
    this.error.set(null);
    this.results.update((values) => ({ ...values, [channel]: undefined }));
    try {
      this.channels.set(await this.api.setChannel(channel, connectionId || null));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The choice could not be saved.');
      await this.load();
    } finally {
      this.busy.set(null);
    }
  }

  protected async test(channel: string): Promise<void> {
    this.testing.set(channel);
    this.results.update((values) => ({ ...values, [channel]: undefined }));
    try {
      const outcome = await this.api.testChannel(channel, this.testTo()[channel].trim());
      this.results.update((values) => ({ ...values, [channel]: outcome }));
    } catch (error) {
      this.results.update((values) => ({ ...values, [channel]: { succeeded: false, message: error instanceof ApiError ? error.message : 'The test could not be sent.' } }));
    } finally {
      this.testing.set(null);
    }
  }

  private async load(): Promise<void> {
    try {
      this.channels.set(await this.api.channels());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Sending settings could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
