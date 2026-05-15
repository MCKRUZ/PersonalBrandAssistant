import { TestBed } from '@angular/core/testing';
import { FeedHubService } from './feed-hub.service';
import { HUB_CONNECTION_FACTORY, HubConnectionFactory } from '../../content/services/signalr.service';
import { FeedItem, FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import { FeedSummary } from '../models/feed-summary.model';

describe('FeedHubService', () => {
  let service: FeedHubService;
  let mockConnection: jasmine.SpyObj<any>;
  let handlers: Record<string, Function>;

  beforeEach(() => {
    handlers = {};
    mockConnection = jasmine.createSpyObj('HubConnection', ['on', 'start', 'stop']);
    mockConnection.on.and.callFake((event: string, handler: Function) => {
      handlers[event] = handler;
    });
    mockConnection.start.and.returnValue(Promise.resolve());
    mockConnection.stop.and.returnValue(Promise.resolve());

    const mockFactory: HubConnectionFactory = (_url: string) => mockConnection;

    TestBed.configureTestingModule({
      providers: [
        FeedHubService,
        { provide: HUB_CONNECTION_FACTORY, useValue: mockFactory },
      ],
    });
    service = TestBed.inject(FeedHubService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('connect() establishes hub connection to /hubs/feed', async () => {
    const factorySpy = jasmine.createSpy('factory').and.returnValue(mockConnection);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        FeedHubService,
        { provide: HUB_CONNECTION_FACTORY, useValue: factorySpy },
      ],
    });
    const svc = TestBed.inject(FeedHubService);

    await svc.connect();

    expect(factorySpy).toHaveBeenCalledWith('/hubs/feed');
    expect(mockConnection.start).toHaveBeenCalled();
  });

  it('connect() registers ReceiveFeedItem and FeedSummaryUpdated handlers', async () => {
    await service.connect();

    expect(mockConnection.on).toHaveBeenCalledWith('ReceiveFeedItem', jasmine.any(Function));
    expect(mockConnection.on).toHaveBeenCalledWith('FeedSummaryUpdated', jasmine.any(Function));
  });

  it('feedItemReceived$ emits when ReceiveFeedItem handler fires', async () => {
    await service.connect();

    const mockItem: FeedItem = {
      id: 'test-id',
      type: FeedItemType.TrendAlert,
      title: 'Test',
      summary: 'Test summary',
      data: null,
      actionType: null,
      actionTargetId: null,
      priority: FeedItemPriority.Normal,
      isRead: false,
      isActedOn: false,
      createdAt: '2026-05-15T00:00:00Z',
      expiresAt: null,
    };

    let emitted: FeedItem | undefined;
    service.feedItemReceived$.subscribe((item) => (emitted = item));

    handlers['ReceiveFeedItem'](mockItem);

    expect(emitted).toEqual(mockItem);
  });

  it('summaryUpdated$ emits when FeedSummaryUpdated handler fires', async () => {
    await service.connect();

    const mockSummary: FeedSummary = {
      unreadCount: 5,
      pendingApprovals: 2,
      trendingCount: 3,
      engagementDelta: 12.5,
    };

    let emitted: FeedSummary | undefined;
    service.summaryUpdated$.subscribe((summary) => (emitted = summary));

    handlers['FeedSummaryUpdated'](mockSummary);

    expect(emitted).toEqual(mockSummary);
  });

  it('disconnect() stops the connection', async () => {
    await service.connect();
    await service.disconnect();

    expect(mockConnection.stop).toHaveBeenCalled();
  });
});
