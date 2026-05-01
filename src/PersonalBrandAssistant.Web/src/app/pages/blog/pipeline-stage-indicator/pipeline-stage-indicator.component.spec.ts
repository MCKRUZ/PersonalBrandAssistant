import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { PipelineStageIndicatorComponent } from './pipeline-stage-indicator.component';
import { BlogPipelineStage, PIPELINE_STAGES } from '../../../features/blog-pipeline/models/blog-pipeline.model';

@Component({
  standalone: true,
  imports: [PipelineStageIndicatorComponent],
  template: `<app-pipeline-stage-indicator [currentStage]="stage()" [disabled]="disabled()" (stageClicked)="clicked = $event" />`,
})
class TestHost {
  stage = signal(BlogPipelineStage.Draft);
  disabled = signal(false);
  clicked: BlogPipelineStage | null = null;
}

describe('PipelineStageIndicatorComponent', () => {
  let fixture: ComponentFixture<TestHost>;
  let host: TestHost;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHost],
    }).compileComponents();
    fixture = TestBed.createComponent(TestHost);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should render all 5 stages', () => {
    const steps = fixture.nativeElement.querySelectorAll('.stage-step');
    expect(steps.length).toBe(PIPELINE_STAGES.length);
  });

  it('should mark first stage as active when Draft', () => {
    const steps = fixture.nativeElement.querySelectorAll('.stage-step');
    expect(steps[0].classList).toContain('active');
    expect(steps[1].classList).toContain('pending');
  });

  it('should mark completed stages with check icon', () => {
    host.stage.set(BlogPipelineStage.Website);
    fixture.detectChanges();
    const steps = fixture.nativeElement.querySelectorAll('.stage-step');
    expect(steps[0].classList).toContain('completed');
    expect(steps[1].classList).toContain('completed');
    expect(steps[2].classList).toContain('completed');
    expect(steps[3].classList).toContain('active');
    expect(steps[4].classList).toContain('pending');
  });

  it('should emit stageClicked when clicking a different stage', () => {
    const steps = fixture.nativeElement.querySelectorAll('.stage-step');
    steps[2].click();
    expect(host.clicked).toBe(BlogPipelineStage.Substack);
  });

  it('should not emit when clicking the current stage', () => {
    const steps = fixture.nativeElement.querySelectorAll('.stage-step');
    steps[0].click();
    expect(host.clicked).toBeNull();
  });

  it('should disable all buttons when disabled input is true', () => {
    host.disabled.set(true);
    fixture.detectChanges();
    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('.stage-step'));
    buttons.forEach(btn => expect(btn.disabled).toBeTrue());
  });

  it('should render connectors between stages', () => {
    const connectors = fixture.nativeElement.querySelectorAll('.step-connector');
    expect(connectors.length).toBe(PIPELINE_STAGES.length - 1);
  });

  it('should mark connectors completed up to current stage', () => {
    host.stage.set(BlogPipelineStage.Substack);
    fixture.detectChanges();
    const connectors = fixture.nativeElement.querySelectorAll('.step-connector');
    expect(connectors[0].classList).toContain('completed');
    expect(connectors[1].classList).toContain('completed');
    expect(connectors[2].classList).not.toContain('completed');
  });
});
