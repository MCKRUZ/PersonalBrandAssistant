import { TestBed } from '@angular/core/testing';
import { HubConnection } from '@microsoft/signalr';
import { SignalRService, HUB_CONNECTION_FACTORY } from './signalr.service';

describe('SignalRService', () => {
  let service: SignalRService;
  let mockConnection: jasmine.SpyObj<HubConnection>;
  let registeredHandlers: Map<string, (...args: unknown[]) => void>;
  let factoryUrl: string | undefined;

  beforeEach(() => {
    registeredHandlers = new Map();
    factoryUrl = undefined;

    mockConnection = jasmine.createSpyObj<HubConnection>('HubConnection', [
      'start',
      'stop',
      'invoke',
      'on',
    ]);
    mockConnection.start.and.returnValue(Promise.resolve());
    mockConnection.stop.and.returnValue(Promise.resolve());
    mockConnection.invoke.and.returnValue(Promise.resolve());
    mockConnection.on.and.callFake((method: string, callback: (...args: unknown[]) => void) => {
      registeredHandlers.set(method, callback);
      return mockConnection;
    });

    TestBed.configureTestingModule({
      providers: [
        SignalRService,
        {
          provide: HUB_CONNECTION_FACTORY,
          useValue: (url: string) => {
            factoryUrl = url;
            return mockConnection;
          },
        },
      ],
    });
    service = TestBed.inject(SignalRService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('connect() establishes hub connection to /hubs/content', async () => {
    await service.connect();

    expect(factoryUrl).toBe('/hubs/content');
    expect(mockConnection.start).toHaveBeenCalled();
  });

  it('connect() registers ReceiveToken, GenerationComplete, GenerationError handlers', async () => {
    await service.connect();

    expect(registeredHandlers.has('ReceiveToken')).toBeTrue();
    expect(registeredHandlers.has('GenerationComplete')).toBeTrue();
    expect(registeredHandlers.has('GenerationError')).toBeTrue();
  });

  it('sendChatMessage invokes hub method with contentId and message', async () => {
    await service.connect();

    const contentId = '123e4567-e89b-12d3-a456-426614174000';
    const message = 'Make it more concise';
    await service.sendChatMessage(contentId, message);

    expect(mockConnection.invoke).toHaveBeenCalledWith('SendChatMessage', contentId, message);
  });

  it('tokens$ emits received tokens', (done) => {
    service.connect().then(() => {
      const emitted: string[] = [];
      const sub = service.tokens$.subscribe((token) => {
        emitted.push(token);
        if (emitted.length === 2) {
          expect(emitted).toEqual(['Hello', ' World']);
          sub.unsubscribe();
          done();
        }
      });

      const handler = registeredHandlers.get('ReceiveToken')!;
      handler('Hello');
      handler(' World');
    });
  });

  it('generationComplete$ emits on completion', (done) => {
    service.connect().then(() => {
      service.generationComplete$.subscribe((fullText) => {
        expect(fullText).toBe('Full generated text');
        done();
      });

      const handler = registeredHandlers.get('GenerationComplete')!;
      handler('Full generated text');
    });
  });

  it('generationError$ emits on error', (done) => {
    service.connect().then(() => {
      service.generationError$.subscribe((error) => {
        expect(error).toBe('Something went wrong');
        done();
      });

      const handler = registeredHandlers.get('GenerationError')!;
      handler('Something went wrong');
    });
  });

  it('disconnect() stops the connection', async () => {
    await service.connect();
    await service.disconnect();

    expect(mockConnection.stop).toHaveBeenCalled();
  });

  it('sendChatMessage throws if not connected', async () => {
    await expectAsync(service.sendChatMessage('id', 'msg')).toBeRejectedWithError(
      'SignalR connection not established'
    );
  });
});
