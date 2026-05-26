import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { NewsHubComponent } from './news-hub.component';

describe('NewsHubComponent', () => {
  let fixture: ComponentFixture<NewsHubComponent>;
  let component: NewsHubComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NewsHubComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideNoopAnimations(), provideRouter([]), MessageService,
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(NewsHubComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  function flushPending() {
    httpMock.match(() => true).forEach(req => {
      if (!req.cancelled) req.flush([]);
    });
  }

  afterEach(() => {
    flushPending();
    httpMock.verify();
  });

  function flushInit() {
    fixture.detectChanges();
    flushPending();
    fixture.detectChanges();
  }

  it('should create', () => {
    flushInit();
    expect(component).toBeTruthy();
  });

  it('should render hero title', () => {
    flushInit();
    const title = fixture.nativeElement.querySelector('.nhub-hero__title');
    expect(title).toBeTruthy();
    expect(title.textContent).toContain('News Hub');
  });

  it('should render live badge', () => {
    flushInit();
    const badge = fixture.nativeElement.querySelector('.nhub-badge');
    expect(badge).toBeTruthy();
  });

  it('should render tabs', () => {
    flushInit();
    const tabs = fixture.nativeElement.querySelectorAll('p-tab');
    expect(tabs.length).toBeGreaterThanOrEqual(3);
  });
});
