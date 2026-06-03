import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { VoiceMeterComponent } from './voice-meter.component';
import { ContentService } from '../../services/content.service';

describe('VoiceMeterComponent', () => {
  let fixture: ComponentFixture<VoiceMeterComponent>;
  let component: VoiceMeterComponent;
  let contentService: jasmine.SpyObj<ContentService>;

  function setup(voiceScore: number | null, feedback: string | null = null) {
    contentService = jasmine.createSpyObj('ContentService', ['voiceCheck']);

    TestBed.configureTestingModule({
      imports: [VoiceMeterComponent],
      providers: [{ provide: ContentService, useValue: contentService }],
    });
    fixture = TestBed.createComponent(VoiceMeterComponent);
    component = fixture.componentInstance;
    const ref = fixture.componentRef;
    ref.setInput('contentId', 'c-1');
    ref.setInput('voiceScore', voiceScore);
    ref.setInput('feedback', feedback);
    fixture.detectChanges();
  }

  it('shows a high-band color and confident note for score >= 80', () => {
    setup(85);
    expect(component.bandColor()).toBe('var(--voice-high)');
    const note = fixture.nativeElement.querySelector('[data-testid="band-note"]');
    expect(note.textContent.toLowerCase()).toContain('sounds like you');
  });

  it('shows a mid-band color and close note for 60 <= score < 80', () => {
    setup(70);
    expect(component.bandColor()).toBe('var(--voice-mid)');
    const note = fixture.nativeElement.querySelector('[data-testid="band-note"]');
    expect(note.textContent.toLowerCase()).toContain('tighten');
  });

  it('shows a low-band color and flat note for score < 60', () => {
    setup(40);
    expect(component.bandColor()).toBe('var(--voice-low)');
    const note = fixture.nativeElement.querySelector('[data-testid="band-note"]');
    expect(note.textContent.toLowerCase()).toContain("doesn't sound like you");
  });

  it('displays the numeric score value', () => {
    setup(73);
    const val = fixture.nativeElement.querySelector('[data-testid="voice-value"]');
    expect(val.textContent.trim()).toContain('73');
  });

  it('re-check calls ContentService.voiceCheck and updates score + feedback', () => {
    setup(40);
    contentService.voiceCheck.and.returnValue(of({ score: 88, feedback: 'Much closer now.' }));
    component.recheck();
    fixture.detectChanges();
    expect(contentService.voiceCheck).toHaveBeenCalledWith('c-1');
    expect(component.displayScore()).toBe(88);
    const note = fixture.nativeElement.querySelector('[data-testid="band-note"]');
    expect(note.textContent).toContain('Much closer now.');
  });
});
