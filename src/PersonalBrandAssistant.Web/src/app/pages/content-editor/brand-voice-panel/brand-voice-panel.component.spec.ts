import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BrandVoicePanelComponent } from './brand-voice-panel.component';
import { BrandVoiceScore } from '../../../core/models/brand-voice.model';

describe('BrandVoicePanelComponent', () => {
  let component: BrandVoicePanelComponent;
  let fixture: ComponentFixture<BrandVoicePanelComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BrandVoicePanelComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(BrandVoicePanelComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should default to no score and not scoring', () => {
    expect(component.score()).toBeUndefined();
    expect(component.isScoring()).toBe(false);
  });

  it('should emit scoreRequested on Score button click', () => {
    let emitted = false;
    component.scoreRequested.subscribe(() => (emitted = true));

    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('p-button button');
    button?.click();
    fixture.detectChanges();

    expect(emitted).toBe(true);
  });
});
