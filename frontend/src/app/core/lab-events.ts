import { Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import type { JobDto, LabDto } from '../api/models';
import { AuthService } from './auth';

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

/**
 * SignalR push channel. Consumers must refetch on `reconnected` because events sent while
 * disconnected are not replayed.
 */
@Injectable({ providedIn: 'root' })
export class LabEvents {
  readonly status = signal<ConnectionStatus>('disconnected');
  readonly labChanged = new Subject<LabDto>();
  readonly jobChanged = new Subject<JobDto>();
  readonly reconnected = new Subject<void>();

  private readonly auth = inject(AuthService);
  private connection?: HubConnection;
  private readonly labs = new Set<string>();
  private retryTimer?: ReturnType<typeof setTimeout>;

  start(): void {
    if (this.connection) {
      return;
    }

    this.connection = new HubConnectionBuilder()
      // Browsers cannot set headers on WebSockets; SignalR sends the token as access_token instead.
      .withUrl(
        '/hubs/labs',
        this.auth.enabled ? { accessTokenFactory: () => this.auth.accessToken() } : {},
      )
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('labChanged', (lab: LabDto) => this.labChanged.next(lab));
    this.connection.on('jobChanged', (job: JobDto) => this.jobChanged.next(job));
    this.connection.onreconnecting(() => this.status.set('reconnecting'));
    this.connection.onreconnected(() => void this.afterConnect(true));
    this.connection.onclose(() => {
      this.status.set('disconnected');
      this.scheduleRetry();
    });

    void this.connect(false);
  }

  async subscribeLab(labId: string): Promise<void> {
    this.labs.add(labId);
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('SubscribeLab', labId).catch(() => undefined);
    }
  }

  async unsubscribeLab(labId: string): Promise<void> {
    this.labs.delete(labId);
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('UnsubscribeLab', labId).catch(() => undefined);
    }
  }

  private async connect(isRetry: boolean): Promise<void> {
    if (!this.connection) {
      return;
    }

    this.status.set('connecting');
    try {
      await this.connection.start();
      await this.afterConnect(isRetry);
    } catch {
      this.status.set('disconnected');
      this.scheduleRetry();
    }
  }

  private async afterConnect(isReconnect: boolean): Promise<void> {
    this.status.set('connected');
    for (const labId of this.labs) {
      await this.connection?.invoke('SubscribeLab', labId).catch(() => undefined);
    }

    if (isReconnect) {
      this.reconnected.next();
    }
  }

  private scheduleRetry(): void {
    clearTimeout(this.retryTimer);
    this.retryTimer = setTimeout(() => void this.connect(true), 5000);
  }
}
