import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DeliveryBadgeComponent } from './delivery-badge.component';
import { PLATFORM_META } from '../../models/platform-metadata';
import { Platform } from '../../models/content.model';

describe('DeliveryBadgeComponent', () => {
  let fixture: ComponentFixture<DeliveryBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DeliveryBadgeComponent] }).compileComponents();
    fixture = TestBed.createComponent(DeliveryBadgeComponent);
  });

  function render(platform: Platform, connected: boolean): HTMLElement {
    fixture.componentRef.setInput('meta', PLATFORM_META[platform]);
    fixture.componentRef.setInput('isConnected', connected);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('.delivery-badge') as HTMLElement;
  }

  it('shows Auto-publish (auto) for an auto+connected platform', () => {
    const el = render(Platform.LinkedIn, true);
    expect(el.textContent).toContain('Auto-publish');
    expect(el.classList).toContain('delivery-badge--auto');
  });

  it('shows Connect (warn) for an auto+disconnected platform', () => {
    const el = render(Platform.Twitter, false);
    expect(el.textContent).toContain('Connect');
    expect(el.classList).toContain('delivery-badge--warn');
  });

  it('shows Manual for a manual platform', () => {
    const el = render(Platform.Medium, true);
    expect(el.textContent).toContain('Manual');
    expect(el.classList).toContain('delivery-badge--manual');
  });
});
