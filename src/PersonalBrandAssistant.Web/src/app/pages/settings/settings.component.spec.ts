import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { SettingsComponent } from './settings.component';
import { SettingsStore } from './settings.store';

describe('SettingsComponent', () => {
  let component: SettingsComponent;
  let fixture: ComponentFixture<SettingsComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SettingsComponent);
    component = fixture.componentInstance;
  });

  function flushInitialLoad() {
    fixture.detectChanges();
    const req = httpMock.expectOne('/api/settings/autonomy');
    req.flush({ globalLevel: 'Draft', autoPublishThreshold: 75 });
    fixture.detectChanges();
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render all three config sections after load', () => {
    flushInitialLoad();
    const brandVoice = fixture.nativeElement.querySelector('app-brand-voice-config');
    const autonomy = fixture.nativeElement.querySelector('app-autonomy-dial');
    const quickPrompts = fixture.nativeElement.querySelector('app-quick-prompts-editor');
    expect(brandVoice).toBeTruthy();
    expect(autonomy).toBeTruthy();
    expect(quickPrompts).toBeTruthy();
  });

  it('should show loading spinner while store is loading', () => {
    fixture.detectChanges();
    const spinner = fixture.nativeElement.querySelector('app-loading-spinner');
    expect(spinner).toBeTruthy();
    const req = httpMock.expectOne('/api/settings/autonomy');
    req.flush({ globalLevel: 'Manual', autoPublishThreshold: 75 });
  });

  it('should show success toast after saving autonomy settings', () => {
    flushInitialLoad();
    const msgService = fixture.debugElement.injector.get(MessageService);
    const spy = spyOn(msgService, 'add');
    component.onAutonomySave({ globalLevel: 'AutoPublish', autoPublishThreshold: 85 });
    const putReq = httpMock.expectOne({ method: 'PUT', url: '/api/settings/autonomy' });
    putReq.flush({ globalLevel: 'AutoPublish', autoPublishThreshold: 85 });
    expect(spy).toHaveBeenCalledWith(jasmine.objectContaining({ severity: 'success', summary: 'Autonomy settings saved' }));
  });

  it('should show success toast after saving brand voice settings', () => {
    flushInitialLoad();
    const msgService = fixture.debugElement.injector.get(MessageService);
    const spy = spyOn(msgService, 'add');
    component.onBrandVoiceSave(component.store.brandProfile());
    expect(spy).toHaveBeenCalledWith(jasmine.objectContaining({ severity: 'success', summary: 'Brand voice settings saved' }));
  });

  afterEach(() => {
    httpMock.verify();
  });
});
