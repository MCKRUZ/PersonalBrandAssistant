import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PreviewTabComponent } from './preview-tab.component';

describe('PreviewTabComponent', () => {
  let component: PreviewTabComponent;
  let fixture: ComponentFixture<PreviewTabComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PreviewTabComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(PreviewTabComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should truncate text exceeding maxLength', () => {
    const long = 'a'.repeat(200);
    expect(component.truncate(long, 100).length).toBeLessThanOrEqual(103);
    expect(component.truncate(long, 100).endsWith('...')).toBe(true);
  });

  it('should not truncate text within maxLength', () => {
    const short = 'hello world';
    expect(component.truncate(short, 100)).toBe(short);
  });
});
