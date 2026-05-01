import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { environment } from '../../environments/environment';

export type ConnectionStatus = 'connected' | 'reconnecting' | 'disconnected';

@Injectable({ providedIn: 'root' })
export class SidecarSignalRService {
  private connection: HubConnection | null = null;
  readonly connectionStatus = signal<ConnectionStatus>('disconnected');

  constructor(private readonly http: HttpClient) {}

  async connect(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return;

    const hubUrl = this.deriveHubUrl();

    this.connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => this.fetchToken(),
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.onreconnecting(() => this.connectionStatus.set('reconnecting'));
    this.connection.onreconnected(() => this.connectionStatus.set('connected'));
    this.connection.onclose(() => this.connectionStatus.set('disconnected'));

    await this.connection.start();
    this.connectionStatus.set('connected');
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
    this.connectionStatus.set('disconnected');
  }

  async invoke(method: string, ...args: unknown[]): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;
    await this.connection.invoke(method, ...args);
  }

  on(method: string, callback: (...args: unknown[]) => void): void {
    this.connection?.on(method, callback);
  }

  private deriveHubUrl(): string {
    return environment.hubUrl;
  }

  private async fetchToken(): Promise<string> {
    const resp = await firstValueFrom(
      this.http.post<{ token: string }>(`${environment.apiUrl}/auth/hub-token`, {}),
    );
    return resp.token;
  }
}
