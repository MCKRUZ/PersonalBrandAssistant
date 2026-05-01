import { ComponentFixture, TestBed } from '@angular/core/testing';
import { VersionsTabComponent } from './versions-tab.component';

describe('VersionsTabComponent', () => {
  let component: VersionsTabComponent;
  let fixture: ComponentFixture<VersionsTabComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [VersionsTabComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(VersionsTabComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('versions', []);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should show empty state when no versions', () => {
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('.empty-versions');
    expect(empty).toBeTruthy();
  });
});
