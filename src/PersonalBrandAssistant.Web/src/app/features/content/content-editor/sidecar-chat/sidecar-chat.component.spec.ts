import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal, NO_ERRORS_SCHEMA, ComponentRef } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject } from 'rxjs';
import { provideMarkdown } from 'ngx-markdown';
import { SidecarChatComponent } from './sidecar-chat.component';
import { ContentEditorStore, ChatMessage } from '../../stores/content-editor.store';
import { SignalRService } from '../../services/signalr.service';
import { ContentDetail, ContentStatus, ContentType, Platform } from '../../models/content.model';

function mockContent(overrides: Partial<ContentDetail> = {}): ContentDetail {
  return {
    id: 'content-1',
    title: 'Test Post',
    body: '# Hello',
    status: ContentStatus.Draft,
    contentType: ContentType.BlogPost,
    primaryPlatform: Platform.Blog,
    voiceScore: 85,
    tags: ['angular'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    viralityPrediction: null,
    sourceIdeaId: null,
    parentContentId: null,
    platformPublishes: [],
    children: [],
    ...overrides,
  };
}

describe('SidecarChatComponent', () => {
  let fixture: ComponentFixture<SidecarChatComponent>;
  let component: SidecarChatComponent;
  let componentRef: ComponentRef<SidecarChatComponent>;
  let signalRService: jasmine.SpyObj<SignalRService>;

  const contentSignal = signal<ContentDetail | null>(mockContent());
  const chatMessagesSignal = signal<ChatMessage[]>([]);
  const isStreamingSignal = signal(false);
  const currentTokensSignal = signal('');

  const mockStore = {
    content: contentSignal,
    chatMessages: chatMessagesSignal,
    isStreaming: isStreamingSignal,
    currentTokens: currentTokensSignal,
    addChatMessage: jasmine.createSpy('addChatMessage'),
    appendToken: jasmine.createSpy('appendToken'),
    completeGeneration: jasmine.createSpy('completeGeneration'),
    applyToEditor: jasmine.createSpy('applyToEditor'),
    hasContent: signal(true),
    loading: signal(false),
    isDirty: signal(false),
    isSaving: signal(false),
    error: signal<string | null>(null),
    canAutoSave: signal(false),
    statusActions: signal([] as string[]),
    loadContent: jasmine.createSpy('loadContent'),
    updateField: jasmine.createSpy('updateField'),
    autoSave: jasmine.createSpy('autoSave'),
    reset: jasmine.createSpy('reset'),
  };

  let tokensSubject: Subject<string>;
  let completeSubject: Subject<string>;
  let errorSubject: Subject<string>;

  function setup(content?: ContentDetail) {
    contentSignal.set(content ?? mockContent());
    chatMessagesSignal.set([]);
    isStreamingSignal.set(false);
    currentTokensSignal.set('');

    tokensSubject = new Subject<string>();
    completeSubject = new Subject<string>();
    errorSubject = new Subject<string>();

    signalRService = jasmine.createSpyObj('SignalRService', [
      'connect', 'disconnect', 'sendChatMessage',
    ], {
      tokens$: tokensSubject.asObservable(),
      generationComplete$: completeSubject.asObservable(),
      generationError$: errorSubject.asObservable(),
    });
    signalRService.connect.and.returnValue(Promise.resolve());
    signalRService.disconnect.and.returnValue(Promise.resolve());
    signalRService.sendChatMessage.and.returnValue(Promise.resolve());

    TestBed.configureTestingModule({
      imports: [SidecarChatComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        provideNoopAnimations(),
        provideMarkdown(),
        { provide: SignalRService, useValue: signalRService },
      ],
    });

    TestBed.overrideComponent(SidecarChatComponent, {
      add: {
        providers: [{ provide: ContentEditorStore, useValue: mockStore }],
      },
    });

    fixture = TestBed.createComponent(SidecarChatComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
    componentRef.setInput('visible', true);
    componentRef.setInput('contentId', 'content-1');
    fixture.detectChanges();
  }

  afterEach(() => {
    mockStore.addChatMessage.calls.reset();
    mockStore.appendToken.calls.reset();
    mockStore.completeGeneration.calls.reset();
    mockStore.applyToEditor.calls.reset();
  });

  it('should create the component', () => {
    setup();
    expect(component).toBeTruthy();
  });

  it('should send message via sendChatMessage on submit', () => {
    setup();
    component.inputMessage.set('Refine the intro');
    component.sendMessage();
    expect(mockStore.addChatMessage).toHaveBeenCalledWith('Refine the intro');
    expect(signalRService.sendChatMessage).toHaveBeenCalledWith('content-1', 'Refine the intro');
  });

  it('should clear input after sending message', () => {
    setup();
    component.inputMessage.set('Refine the intro');
    component.sendMessage();
    expect(component.inputMessage()).toBe('');
  });

  it('should not send empty messages', () => {
    setup();
    component.inputMessage.set('   ');
    component.sendMessage();
    expect(mockStore.addChatMessage).not.toHaveBeenCalled();
    expect(signalRService.sendChatMessage).not.toHaveBeenCalled();
  });

  it('should not send when streaming', () => {
    setup();
    isStreamingSignal.set(true);
    component.inputMessage.set('test');
    component.sendMessage();
    expect(signalRService.sendChatMessage).not.toHaveBeenCalled();
  });

  it('should display streaming area when tokens are present', () => {
    setup();
    isStreamingSignal.set(true);
    currentTokensSignal.set('Hello world');
    fixture.detectChanges();
    const streaming = fixture.nativeElement.querySelector('[data-testid="streaming-area"]');
    expect(streaming).toBeTruthy();
    const shimmer = fixture.nativeElement.querySelector('[data-testid="skeleton-shimmer"]');
    expect(shimmer).toBeFalsy();
  });

  it('should show skeleton shimmer before first token', () => {
    setup();
    isStreamingSignal.set(true);
    currentTokensSignal.set('');
    fixture.detectChanges();
    const shimmer = fixture.nativeElement.querySelector('[data-testid="skeleton-shimmer"]');
    expect(shimmer).toBeTruthy();
  });

  it('should show action buttons on completed assistant message', () => {
    setup();
    chatMessagesSignal.set([
      { role: 'assistant', content: 'Generated text', timestamp: '2026-01-01T00:00:00Z' },
    ]);
    fixture.detectChanges();
    const applyBtn = fixture.nativeElement.querySelector('[data-testid="apply-btn"]');
    const copyBtn = fixture.nativeElement.querySelector('[data-testid="copy-btn"]');
    expect(applyBtn).toBeTruthy();
    expect(copyBtn).toBeTruthy();
  });

  it('should call applyToEditor when Apply button clicked', () => {
    setup();
    chatMessagesSignal.set([
      { role: 'assistant', content: 'New body text', timestamp: '2026-01-01T00:00:00Z' },
    ]);
    fixture.detectChanges();
    component.applyToEditor('New body text');
    expect(mockStore.applyToEditor).toHaveBeenCalledWith('New body text');
  });

  it('should show draft chips when editor body is empty', () => {
    setup(mockContent({ body: '' }));
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('[data-testid="quick-chip"]');
    const labels = Array.from(chips).map((c: any) => c.textContent.trim());
    expect(labels).toContain('Draft from idea');
    expect(labels).toContain('Draft from scratch');
  });

  it('should show refine chips when editor has content', () => {
    setup(mockContent({ body: '# Hello world' }));
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('[data-testid="quick-chip"]');
    const labels = Array.from(chips).map((c: any) => c.textContent.trim());
    expect(labels).toContain('Refine');
    expect(labels).toContain('Shorten');
  });

  it('should call disconnect and complete partial on stopGeneration', async () => {
    setup();
    currentTokensSignal.set('partial text');
    await component.stopGeneration();
    expect(signalRService.disconnect).toHaveBeenCalled();
    expect(mockStore.completeGeneration).toHaveBeenCalledWith('partial text');
    expect(signalRService.connect).toHaveBeenCalled();
  });

  it('should handle generationError by completing with error message', () => {
    setup();
    currentTokensSignal.set('');
    errorSubject.next('Some error');
    expect(mockStore.completeGeneration).toHaveBeenCalledWith('Error: generation failed');
  });

  it('should handle Enter key to send message', () => {
    setup();
    component.inputMessage.set('test message');
    const event = new KeyboardEvent('keydown', { key: 'Enter', shiftKey: false });
    spyOn(event, 'preventDefault');
    component.onKeydown(event);
    expect(event.preventDefault).toHaveBeenCalled();
    expect(mockStore.addChatMessage).toHaveBeenCalledWith('test message');
  });

  it('should not send on Shift+Enter', () => {
    setup();
    component.inputMessage.set('test message');
    const event = new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true });
    component.onKeydown(event);
    expect(mockStore.addChatMessage).not.toHaveBeenCalled();
  });
});
