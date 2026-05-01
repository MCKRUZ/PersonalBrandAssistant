import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { BlogEditorComponent } from './blog-editor.component';
import { DraftApplyService } from '../../shell/sidecar/draft-apply.service';
import { BlogPipelineStage } from '../../features/blog-pipeline/models/blog-pipeline.model';

const API = 'http://localhost:5000/api';

const mockContent = {
  id: 'blog-1', title: 'Test', body: '<p>Body</p>',
  type: 'BlogPost', status: 'Draft', platform: 'PersonalBlog',
  createdAt: '2026-01-01', updatedAt: '2026-01-01', version: 1,
  capturedAutonomyLevel: 'Draft',
};

const mockPipeline = [{
  id: 'blog-1', title: 'Test', status: 'Draft', createdAt: '2026-01-01',
  currentBlogStage: BlogPipelineStage.Image, blogStageHistory: [],
  substackPostUrl: null, blogPostUrl: null, blogSkipped: false,
}];

describe('BlogEditorComponent', () => {
  let fixture: ComponentFixture<BlogEditorComponent>;
  let component: BlogEditorComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BlogEditorComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        DraftApplyService,
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: (key: string) => key === 'id' ? 'blog-1' : null } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(BlogEditorComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function loadEditor() {
    fixture.detectChanges();
    httpMock.expectOne(`${API}/content/blog-1`).flush(mockContent);
    httpMock.expectOne(`${API}/blog-pipeline`).flush(mockPipeline);
    fixture.detectChanges();
  }

  it('should create', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${API}/content/blog-1`).flush(mockContent);
    httpMock.expectOne(`${API}/blog-pipeline`).flush(mockPipeline);
    expect(component).toBeTruthy();
  });

  it('should show loading spinner initially', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-loading-spinner')).toBeTruthy();
    httpMock.expectOne(`${API}/content/blog-1`).flush(mockContent);
    httpMock.expectOne(`${API}/blog-pipeline`).flush(mockPipeline);
  });

  it('should display title input after loading', fakeAsync(() => {
    loadEditor();
    tick();
    const titleInput = fixture.nativeElement.querySelector('.title-input');
    expect(titleInput).toBeTruthy();
    expect(titleInput.value).toBe('Test');
  }));

  it('should show pipeline stage indicator', fakeAsync(() => {
    loadEditor();
    tick();
    expect(fixture.nativeElement.querySelector('app-pipeline-stage-indicator')).toBeTruthy();
  }));

  it('should show advance stage button', fakeAsync(() => {
    loadEditor();
    tick();
    const advanceBtn = fixture.nativeElement.querySelector('.pipeline-actions p-button');
    expect(advanceBtn).toBeTruthy();
  }));

  it('should show skipped badge when blog is skipped', fakeAsync(() => {
    fixture.detectChanges();
    httpMock.expectOne(`${API}/content/blog-1`).flush(mockContent);
    httpMock.expectOne(`${API}/blog-pipeline`).flush([{ ...mockPipeline[0], blogSkipped: true }]);
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.skipped-badge')).toBeTruthy();
  }));
});
