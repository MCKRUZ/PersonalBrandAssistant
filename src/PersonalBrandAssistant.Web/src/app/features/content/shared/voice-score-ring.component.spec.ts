import { ComponentFixture, TestBed } from '@angular/core/testing';
import { VoiceScoreRingComponent } from './voice-score-ring.component';

describe('VoiceScoreRingComponent', () => {
  let fixture: ComponentFixture<VoiceScoreRingComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [VoiceScoreRingComponent] }).compileComponents();
    fixture = TestBed.createComponent(VoiceScoreRingComponent);
  });

  it('shows the numeric score and the high-band color when score >= 80', () => {
    fixture.componentRef.setInput('score', 85);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent?.trim()).toBe('85');
    expect((el.querySelector('.inner') as HTMLElement).style.color).toContain('--voice-high');
  });

  it('renders a dashed-empty ring with no number when score is null', () => {
    fixture.componentRef.setInput('score', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.ring.empty')).toBeTruthy();
    expect(el.querySelector('.inner')).toBeNull();
  });

  it('respects the size input', () => {
    fixture.componentRef.setInput('score', 50);
    fixture.componentRef.setInput('size', 32);
    fixture.detectChanges();
    const ring = (fixture.nativeElement as HTMLElement).querySelector('.ring') as HTMLElement;
    expect(ring.style.width).toBe('32px');
  });
});
