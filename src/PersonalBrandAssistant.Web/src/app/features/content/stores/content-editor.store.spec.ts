import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ContentEditorStore } from './content-editor.store';
import { ContentService } from '../services/content.service';
import {
  ContentStatus,
  ContentType,
  Platform,
} from '../models/content.model';
import type { ContentDetail } from '../models/content.model';

describe('ContentEditorStore', () => {
  let store: InstanceType<typeof ContentEditorStore>;
  let contentService: jasmine.SpyObj<ContentService>;

  const mockDetail: ContentDetail = {
    id: 'content-1',
    title: 'Test Content',
    body: 'Original body',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: 0.85,
    viralityPrediction: null,
    sourceIdeaId: null,
    parentContentId: null,
    tags: ['test'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T12:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    children: [],
  };

  beforeEach(() => {
    contentService = jasmine.createSpyObj('ContentService', [
      'get',
      'update',
    ]);
    contentService.get.and.returnValue(of(mockDetail));
    contentService.update.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      providers: [
        ContentEditorStore,
        { provide: ContentService, useValue: contentService },
      ],
    });
    store = TestBed.inject(ContentEditorStore);
  });

  it('has correct initial state', () => {
    expect(store.content()).toBeNull();
    expect(store.isDirty()).toBeFalse();
    expect(store.isSaving()).toBeFalse();
    expect(store.chatMessages()).toEqual([]);
    expect(store.isStreaming()).toBeFalse();
    expect(store.currentTokens()).toBe('');
    expect(store.loading()).toBeFalse();
    expect(store.error()).toBeNull();
  });

  it('loadContent fetches and sets content', () => {
    store.loadContent('content-1');

    expect(contentService.get).toHaveBeenCalledWith('content-1');
    expect(store.content()).toEqual(mockDetail);
    expect(store.loading()).toBeFalse();
    expect(store.isDirty()).toBeFalse();
    expect(store.chatMessages()).toEqual([]);
  });

  it('updateField marks dirty', () => {
    store.loadContent('content-1');

    store.updateField('title', 'Updated title');

    expect(store.content()!.title).toBe('Updated title');
    expect(store.isDirty()).toBeTrue();
  });

  it('autoSave calls service with lastUpdatedAt', () => {
    store.loadContent('content-1');
    store.updateField('body', 'New body');

    store.autoSave();

    expect(contentService.update).toHaveBeenCalledWith('content-1', {
      title: 'Test Content',
      body: 'New body',
      tags: ['test'],
      contentType: ContentType.BlogPost,
      primaryPlatform: Platform.Blog,
      targetPlatforms: [Platform.Blog],
      lastUpdatedAt: '2026-01-01T12:00:00Z',
    });
  });

  it('autoSave clears dirty on success', () => {
    store.loadContent('content-1');
    store.updateField('body', 'New body');

    store.autoSave();

    expect(store.isDirty()).toBeFalse();
    expect(store.isSaving()).toBeFalse();
  });

  it('autoSave sets error on failure', () => {
    contentService.update.and.returnValue(
      throwError(() => new Error('Conflict'))
    );
    store.loadContent('content-1');
    store.updateField('body', 'New body');

    store.autoSave();

    expect(store.error()).toBe('Conflict');
    expect(store.isDirty()).toBeTrue();
    expect(store.isSaving()).toBeFalse();
  });

  it('autoSave skips when not dirty', () => {
    store.loadContent('content-1');

    store.autoSave();

    expect(contentService.update).not.toHaveBeenCalled();
  });

  it('appendToken accumulates tokens', () => {
    store.appendToken('Hello');
    expect(store.currentTokens()).toBe('Hello');
    expect(store.isStreaming()).toBeTrue();

    store.appendToken(' world');
    expect(store.currentTokens()).toBe('Hello world');
  });

  it('completeGeneration finalizes message', () => {
    store.appendToken('Hello');
    store.completeGeneration('Hello world');

    expect(store.isStreaming()).toBeFalse();
    expect(store.currentTokens()).toBe('');
    expect(store.chatMessages().length).toBe(1);
    expect(store.chatMessages()[0].role).toBe('assistant');
    expect(store.chatMessages()[0].content).toBe('Hello world');
  });

  it('applyToEditor replaces body and marks dirty', () => {
    store.loadContent('content-1');

    store.applyToEditor('AI-generated body');

    expect(store.content()!.body).toBe('AI-generated body');
    expect(store.isDirty()).toBeTrue();
  });

  it('reset clears all state', () => {
    store.loadContent('content-1');
    store.updateField('body', 'Changed');
    store.appendToken('token');

    store.reset();

    expect(store.content()).toBeNull();
    expect(store.isDirty()).toBeFalse();
    expect(store.chatMessages()).toEqual([]);
    expect(store.isStreaming()).toBeFalse();
    expect(store.currentTokens()).toBe('');
  });

  it('hasContent computed is true when content loaded', () => {
    expect(store.hasContent()).toBeFalse();

    store.loadContent('content-1');

    expect(store.hasContent()).toBeTrue();
  });

  it('addChatMessage adds user message', () => {
    store.addChatMessage('Help me rewrite this');

    expect(store.chatMessages().length).toBe(1);
    expect(store.chatMessages()[0].role).toBe('user');
    expect(store.chatMessages()[0].content).toBe('Help me rewrite this');
  });

  it('loadContent sets error on failure', () => {
    contentService.get.and.returnValue(
      throwError(() => new Error('Not found'))
    );

    store.loadContent('bad-id');

    expect(store.loading()).toBeFalse();
    expect(store.error()).toBe('Not found');
    expect(store.content()).toBeNull();
  });

  it('canAutoSave reflects dirty and saving state', () => {
    expect(store.canAutoSave()).toBeFalse();

    store.loadContent('content-1');
    expect(store.canAutoSave()).toBeFalse();

    store.updateField('body', 'Changed');
    expect(store.canAutoSave()).toBeTrue();
  });

  it('statusActions returns correct actions for Draft status', () => {
    store.loadContent('content-1');

    expect(store.statusActions()).toEqual(['submitForReview']);
  });

  it('statusActions returns correct actions for Review status', () => {
    contentService.get.and.returnValue(
      of({ ...mockDetail, status: ContentStatus.Review })
    );
    store.loadContent('content-1');

    expect(store.statusActions()).toEqual(['approve', 'requestChanges']);
  });

  it('statusActions returns empty when no content', () => {
    expect(store.statusActions()).toEqual([]);
  });
});
