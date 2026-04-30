import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { SidecarComponent } from './sidecar.component';
import { SidecarStore } from './sidecar.store';

describe('SidecarComponent', () => {
  let fixture: ComponentFixture<SidecarComponent>;
  let component: SidecarComponent;
  let store: InstanceType<typeof SidecarStore>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SidecarComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    store = TestBed.inject(SidecarStore);
    spyOn(store, 'connect').and.returnValue(Promise.resolve());
    spyOn(store, 'disconnect').and.returnValue(Promise.resolve());
    spyOn(store, 'initRouteTracking');

    fixture = TestBed.createComponent(SidecarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call connect and initRouteTracking on init', () => {
    expect(store.initRouteTracking).toHaveBeenCalled();
    expect(store.connect).toHaveBeenCalled();
  });

  it('should display context label', () => {
    const label = fixture.debugElement.query(By.css('.context-label'));
    expect(label.nativeElement.textContent).toContain('In context:');
  });

  it('should render quick prompt chips', () => {
    const chips = fixture.debugElement.queryAll(By.css('app-quick-prompt-chip'));
    expect(chips.length).toBeGreaterThan(0);
  });

  it('should render composer textarea', () => {
    const textarea = fixture.debugElement.query(By.css('textarea'));
    expect(textarea).toBeTruthy();
    expect(textarea.nativeElement.placeholder).toBe('Ask Claude...');
  });

  it('should show disconnected banner when status is disconnected', () => {
    const banner = fixture.debugElement.query(By.css('.status-banner.disconnected'));
    expect(banner).toBeTruthy();
    expect(banner.nativeElement.textContent).toContain('Connection lost');
  });

  it('should render send button', () => {
    const sendBtn = fixture.debugElement.query(By.css('.composer p-button'));
    expect(sendBtn).toBeTruthy();
  });

  it('should render model label', () => {
    const label = fixture.debugElement.query(By.css('.model-label'));
    expect(label.nativeElement.textContent).toContain('Claude 3.5 Sonnet');
  });

  it('should not send empty messages', () => {
    component.composerText = '   ';
    component.send();
    expect(component.store.messages().length).toBe(0);
  });
});
