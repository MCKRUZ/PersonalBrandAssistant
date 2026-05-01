import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MessageService } from 'primeng/api';
import { SocialHubComponent } from './social-hub.component';

describe('SocialHubComponent', () => {
  let fixture: ComponentFixture<SocialHubComponent>;
  let component: SocialHubComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SocialHubComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), MessageService],
    }).compileComponents();
    fixture = TestBed.createComponent(SocialHubComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(() => true).forEach(req => {
      if (!req.cancelled) req.flush([]);
    });
    httpMock.verify();
  });

  function flushInit() {
    fixture.detectChanges();
    httpMock.match(() => true).forEach(req => req.flush([]));
    fixture.detectChanges();
  }

  it('should create', () => {
    flushInit();
    expect(component).toBeTruthy();
  });

  it('should render page header with breadcrumb and title', () => {
    flushInit();
    const el = fixture.nativeElement;
    expect(el.querySelector('.breadcrumb').textContent).toContain('ENGAGEMENT');
    expect(el.querySelector('h1').textContent).toContain('Social');
    expect(el.querySelector('.subtitle').textContent).toContain('Community engagement');
  });

  it('should render three tabs', () => {
    flushInit();
    const tabs = fixture.nativeElement.querySelectorAll('p-tab');
    expect(tabs.length).toBe(3);
  });

  it('should update active tab on tab change', () => {
    flushInit();
    component.onTabChange('inbox');
    expect(component.store.activeTab()).toBe('inbox');
  });
});
