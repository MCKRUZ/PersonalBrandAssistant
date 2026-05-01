import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { BlogPipelineApiService } from './blog-pipeline-api.service';
import { BlogPipelineStage } from '../../features/blog-pipeline/models/blog-pipeline.model';

const API = 'http://localhost:5000/api';

describe('BlogPipelineApiService', () => {
  let service: BlogPipelineApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BlogPipelineApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should get pipeline item by content ID', () => {
    const items = [
      { id: 'abc', title: 'Post 1', status: 'Draft', createdAt: '', currentBlogStage: 0, blogStageHistory: [], substackPostUrl: null, blogPostUrl: null, blogSkipped: false },
      { id: 'xyz', title: 'Post 2', status: 'Draft', createdAt: '', currentBlogStage: 1, blogStageHistory: [], substackPostUrl: null, blogPostUrl: null, blogSkipped: false },
    ];
    service.getById('abc').subscribe(result => {
      expect(result.id).toBe('abc');
    });
    httpMock.expectOne(`${API}/blog-pipeline`).flush(items);
  });

  it('should advance stage with optional note', () => {
    service.advanceStage('abc', 'ready').subscribe(result => {
      expect(result.currentBlogStage).toBe(BlogPipelineStage.Image);
    });
    const req = httpMock.expectOne(`${API}/blog-pipeline/abc/advance`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ note: 'ready' });
    req.flush({ currentBlogStage: BlogPipelineStage.Image });
  });

  it('should set specific stage', () => {
    service.setStage('abc', BlogPipelineStage.Website, 'deployed').subscribe();
    const req = httpMock.expectOne(`${API}/blog-pipeline/abc/stage`);
    expect(req.request.body).toEqual({ stage: BlogPipelineStage.Website, note: 'deployed' });
    req.flush({ currentBlogStage: BlogPipelineStage.Website });
  });

  it('should confirm schedule', () => {
    service.confirmSchedule('abc').subscribe(result => {
      expect(result.scheduledAt).toBeTruthy();
    });
    const req = httpMock.expectOne(`${API}/blog-pipeline/abc/schedule`);
    expect(req.request.method).toBe('POST');
    req.flush({ scheduledAt: '2026-05-01T10:00:00Z' });
  });

  it('should update delay', () => {
    service.updateDelay('abc', 7).subscribe(result => {
      expect(result.blogDelayDays).toBe(7);
    });
    const req = httpMock.expectOne(`${API}/blog-pipeline/abc/delay`);
    expect(req.request.body).toEqual({ delayDays: 7 });
    req.flush({ blogDelayDays: 7 });
  });

  it('should skip blog', () => {
    service.skipBlog('abc').subscribe(result => {
      expect(result.blogSkipped).toBe(true);
    });
    const req = httpMock.expectOne(`${API}/blog-pipeline/abc/skip-blog`);
    expect(req.request.method).toBe('POST');
    req.flush({ blogSkipped: true });
  });
});
