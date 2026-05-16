import { TestBed } from '@angular/core/testing';
import { FeedHubService } from './feed-hub.service';
import { HUB_CONNECTION_FACTORY } from '../../content/services/signalr.service';
import type { HubConnection } from '@microsoft/signalr';
import { mockFeedItem, mockFeedSummary } from '../testing/feed-test-utils';

describe('FeedHubService', () => {
  let service: FeedHubService;
  let mockConnection: jasmine.SpyObj<HubConnection>;
  let handlers: Map<string, Function>;
  let mockFactory: jasmine.Spy;

  beforeEach(() => {
    handlers = new Map<string, Function>();
    mockConnection = jasmine.createSpyObj<HubConnection>('HubConnection', ['start', 'stop', 'on']);
    mockConnection.on.and.callFake((event: string, handler: Function) => {
      handlers.set(event, handler);
    });
    mockConnection.start.and.returnValue(Promise.resolve());
    mockConnection.stop.and.returnValue(Promise.resolve());
    mockFactory = jasmine.createSpy('HubConnectionFactory').and.returnValue(mockConnection);

    TestBed.configureTestingModule({
      providers: [
        FeedHubService,
        { provide: HUB_CONNECTION_FACTORY, useValue: mockFactory },
      ],
    });
    service = TestBed.inject(FeedHubService);
  });

  it('should connect() and create HubConnection to /hubs/feed', async () => {
    await service.connect();

    expect(mockFactory).toHaveBeenCalledWith('/hubs/feed');
    expect(mockConnection.start).toHaveBeenCalled();
  });

  it('should emit on feedItemReceived$ when ReceiveFeedItem handler fires', async () => {
    await service.connect();

    const item = mockFeedItem({ title: 'SignalR Item' });
    let emitted: unknown;
    service.feedItemReceived$.subscribe((value) => (emitted = value));

    const handler = handlers.get('ReceiveFeedItem');
    expect(handler).toBeDefined();
    handler!(item);

    expect(emitted).toEqual(item);
  });

  it('should emit on summaryUpdated$ when FeedSummaryUpdated handler fires', async () => {
    await service.connect();

    const summary = mockFeedSummary({ unreadCount: 99 });
    let emitted: unknown;
    service.summaryUpdated$.subscribe((value) => (emitted = value));

    const handler = handlers.get('FeedSummaryUpdated');
    expect(handler).toBeDefined();
    handler!(summary);

    expect(emitted).toEqual(summary);
  });

  it('should disconnect() and stop connection', async () => {
    await service.connect();
    await service.disconnect();

    expect(mockConnection.stop).toHaveBeenCalled();
  });
});
