import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { BlogEditorStore } from './blog-editor.store';
import { ContentEditorApiService } from '../content-editor/content-editor-api.service';
import { BlogPipelineStage } from '../../features/blog-pipeline/models/blog-pipeline.model';

const API = 'http://localhost:5000/api';

const mockContent = {
  id: 'blog-123', title: 'Test Blog', body: '<p>Hello</p>',
  type: 'BlogPost', status: 'Draft', platform: 'PersonalBlog',
  createdAt: '2026-01-01', updatedAt: '2026-01-01', version: 1,
  capturedAutonomyLevel: 'Draft',
};

const mockPipeline = [
  {
    id: 'blog-123', title: 'Test Blog', status: 'Draft', createdAt: '2026-01-01',
    currentBlogStage: BlogPipelineStage.Draft, blogStageHistory: [],
    substackPostUrl: null, blogPostUrl: null, blogSkipped: false,
  },
];

describe('BlogEditorStore', () => {
  let store: InstanceType<typeof BlogEditorStore>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [BlogEditorStore, ContentEditorApiService, provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(BlogEditorStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function loadContent() {
    store.loadContent('blog-123');
    httpMock.expectOne(`${API}/content/blog-123`).flush(mockContent);
    httpMock.expectOne(`${API}/blog-pipeline`).flush(mockPipeline);
  }

  it('should have initial state', () => {
    expect(store.content()).toBeUndefined();
    expect(store.currentBlogStage()).toBe(BlogPipelineStage.Draft);
    expect(store.isLoading()).toBe(false);
  });

  it('should load content and pipeline data', fakeAsync(() => {
    loadContent();
    tick();
    expect(store.content()!.id).toBe('blog-123');
    expect(store.currentBlogStage()).toBe(BlogPipelineStage.Draft);
    expect(store.isLoading()).toBe(false);
  }));

  it('should advance stage', fakeAsync(() => {
    loadContent();
    tick();
    store.advanceStage();
    expect(store.isAdvancing()).toBe(true);
    httpMock.expectOne(`${API}/blog-pipeline/blog-123/advance`).flush({ currentBlogStage: BlogPipelineStage.Image });
    tick();
    expect(store.currentBlogStage()).toBe(BlogPipelineStage.Image);
    expect(store.isAdvancing()).toBe(false);
  }));

  it('should not advance past Social stage', fakeAsync(() => {
    store.loadContent('blog-123');
    httpMock.expectOne(`${API}/content/blog-123`).flush(mockContent);
    httpMock.expectOne(`${API}/blog-pipeline`).flush([{
      ...mockPipeline[0], currentBlogStage: BlogPipelineStage.Social,
    }]);
    tick();
    store.advanceStage();
    httpMock.expectNone(`${API}/blog-pipeline/blog-123/advance`);
  }));

  it('should set specific stage', fakeAsync(() => {
    loadContent();
    tick();
    store.setStage(BlogPipelineStage.Substack, 'skip ahead');
    httpMock.expectOne(`${API}/blog-pipeline/blog-123/stage`).flush({ currentBlogStage: BlogPipelineStage.Substack });
    tick();
    expect(store.currentBlogStage()).toBe(BlogPipelineStage.Substack);
  }));

  it('should update delay', fakeAsync(() => {
    loadContent();
    tick();
    store.updateDelay(7);
    httpMock.expectOne(`${API}/blog-pipeline/blog-123/delay`).flush({ blogDelayDays: 7 });
    tick();
    expect(store.blogDelayDays()).toBe(7);
  }));

  it('should skip blog', fakeAsync(() => {
    loadContent();
    tick();
    store.skipBlog();
    httpMock.expectOne(`${API}/blog-pipeline/blog-123/skip-blog`).flush({ blogSkipped: true });
    tick();
    expect(store.blogSkipped()).toBe(true);
  }));

  it('should compute isLastStage', fakeAsync(() => {
    store.loadContent('blog-123');
    httpMock.expectOne(`${API}/content/blog-123`).flush(mockContent);
    httpMock.expectOne(`${API}/blog-pipeline`).flush([{
      ...mockPipeline[0], currentBlogStage: BlogPipelineStage.Social,
    }]);
    tick();
    expect(store.isLastStage()).toBe(true);
  }));
});
