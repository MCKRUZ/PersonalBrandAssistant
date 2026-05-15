import { inject, Injectable, InjectionToken } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';

export type HubConnectionFactory = (url: string) => HubConnection;

export const HUB_CONNECTION_FACTORY = new InjectionToken<HubConnectionFactory>(
  'HubConnectionFactory',
  {
    providedIn: 'root',
    factory: () => (url: string) =>
      new HubConnectionBuilder().withUrl(url).withAutomaticReconnect().build(),
  }
);

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private readonly connectionFactory = inject(HUB_CONNECTION_FACTORY);
  private connection: HubConnection | null = null;

  private readonly tokensSubject = new Subject<string>();
  private readonly generationCompleteSubject = new Subject<string>();
  private readonly generationErrorSubject = new Subject<string>();

  readonly tokens$: Observable<string> = this.tokensSubject.asObservable();
  readonly generationComplete$: Observable<string> = this.generationCompleteSubject.asObservable();
  readonly generationError$: Observable<string> = this.generationErrorSubject.asObservable();

  async connect(): Promise<void> {
    if (this.connection) {
      await this.disconnect();
    }
    this.connection = this.connectionFactory('/hubs/content');

    this.connection.on('ReceiveToken', (token: string) => {
      this.tokensSubject.next(token);
    });

    this.connection.on('GenerationComplete', (fullText: string) => {
      this.generationCompleteSubject.next(fullText);
    });

    this.connection.on('GenerationError', (error: string) => {
      this.generationErrorSubject.next(error);
    });

    await this.connection.start();
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }

  async sendChatMessage(contentId: string, message: string): Promise<void> {
    if (!this.connection) {
      throw new Error('SignalR connection not established');
    }
    await this.connection.invoke('SendChatMessage', contentId, message);
  }
}
