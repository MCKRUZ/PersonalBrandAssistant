import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmationService } from 'primeng/api';
import { AutonomyDialComponent } from './autonomy-dial.component';
import { AutonomySettings } from '../../../core/models/autonomy.model';

describe('AutonomyDialComponent', () => {
  let component: AutonomyDialComponent;
  let fixture: ComponentFixture<AutonomyDialComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AutonomyDialComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(AutonomyDialComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render 5-level slider with descriptions', () => {
    fixture.detectChanges();
    const slider = fixture.nativeElement.querySelector('p-slider');
    expect(slider).toBeTruthy();
    const label = fixture.nativeElement.querySelector('.level-label');
    expect(label.textContent).toContain('Manual');
    const desc = fixture.nativeElement.querySelector('.level-desc');
    expect(desc.textContent).toContain('You initiate everything');
  });

  it('should show threshold input when AutoPublish selected', () => {
    component.levelIndex.set(3);
    fixture.detectChanges();
    const thresholdSection = fixture.nativeElement.querySelector('.threshold-section');
    expect(thresholdSection).toBeTruthy();
    const inputNumber = fixture.nativeElement.querySelector('p-inputNumber');
    expect(inputNumber).toBeTruthy();
  });

  it('should not show threshold input for other levels', () => {
    component.levelIndex.set(1);
    fixture.detectChanges();
    const thresholdSection = fixture.nativeElement.querySelector('.threshold-section');
    expect(thresholdSection).toBeFalsy();
  });

  it('should show confirmation dialog when FullAuto selected', () => {
    fixture.detectChanges();
    const confirmService = fixture.debugElement.injector.get(ConfirmationService);
    const spy = spyOn(confirmService, 'confirm');
    component.onLevelChange(4);
    expect(spy).toHaveBeenCalledWith(jasmine.objectContaining({
      header: 'Enable Full Autonomy?',
    }));
  });

  it('should emit autonomyChange event on save', () => {
    fixture.detectChanges();
    component.levelIndex.set(2);
    component.threshold.set(90);
    const spy = spyOn(component.autonomyChange, 'emit');
    component.save();
    expect(spy).toHaveBeenCalledWith({ globalLevel: 'Draft', autoPublishThreshold: 90 });
  });

  it('should disable save button when form is pristine', () => {
    fixture.componentRef.setInput('autonomy', { globalLevel: 'Manual', autoPublishThreshold: 75 } as AutonomySettings);
    fixture.detectChanges();
    expect(component.dirty()).toBe(false);
  });

  it('should detect dirty state when level changes', () => {
    fixture.componentRef.setInput('autonomy', { globalLevel: 'Manual', autoPublishThreshold: 75 } as AutonomySettings);
    fixture.detectChanges();
    component.levelIndex.set(2);
    expect(component.dirty()).toBe(true);
  });
});
