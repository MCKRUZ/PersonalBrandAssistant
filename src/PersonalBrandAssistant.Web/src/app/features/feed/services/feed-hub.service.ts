import { inject, Injectable } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { HubConnection } from '@microsoft/signalr';
import { HUB_CONNECTION_FACTORY } from '../../content/services/signalr.service';
import { FeedItem } from '../models/feed-item.model';
import { FeedSummary } from '../models/feed-summary.model';

@Injectable({ providedIn: 'root' })
export class FeedHubService {
  private readonly connectionFactory = inject(HUB_CONNECTION_FACTORY);
  private connection: HubConnection | null = null;

  private readonly feedItemSubject = new Subject<FeedItem>();
  private readonly summarySubject = new Subject<FeedSummary>();

  readonly feedItemReceived$: Observable<FeedItem> = this.feedItemSubject.asObservable();
  readonly summaryUpdated$: Observable<FeedSummary> = this.summarySubject.asObservable();

  async connect(): Promise<void> {
    if (this.connection) {
      await this.disconnect();
    }
    this.connection = this.connectionFactory('/hubs/feed');

    this.connection.on('ReceiveFeedItem', (item: FeedItem) => {
      this.feedItemSubject.next(item);
    });

    this.connection.on('FeedSummaryUpdated', (summary: FeedSummary) => {
      this.summarySubject.next(summary);
    });

    await this.connection.start();
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }
}
