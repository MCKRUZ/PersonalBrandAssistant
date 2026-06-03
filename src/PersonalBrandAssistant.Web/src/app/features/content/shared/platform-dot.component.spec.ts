import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlatformDotComponent } from './platform-dot.component';
import { Platform } from '../models/content.model';

describe('PlatformDotComponent', () => {
  let fixture: ComponentFixture<PlatformDotComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PlatformDotComponent] }).compileComponents();
    fixture = TestBed.createComponent(PlatformDotComponent);
  });

  it('renders a 2-letter code tile in tile variant', () => {
    fixture.componentRef.setInput('platform', Platform.LinkedIn);
    fixture.componentRef.setInput('variant', 'tile');
    fixture.detectChanges();
    const tile = (fixture.nativeElement as HTMLElement).querySelector('.tile');
    expect(tile?.textContent?.trim()).toBe('Li');
  });

  it('renders a colored dot in dot variant', () => {
    fixture.componentRef.setInput('platform', Platform.Twitter);
    fixture.componentRef.setInput('variant', 'dot');
    fixture.detectChanges();
    const dot = (fixture.nativeElement as HTMLElement).querySelector('.dot');
    expect(dot).toBeTruthy();
    expect((fixture.nativeElement as HTMLElement).querySelector('.tile')).toBeNull();
  });

  it('uses different colors per platform', () => {
    fixture.componentRef.setInput('variant', 'dot');
    fixture.componentRef.setInput('platform', Platform.Blog);
    fixture.detectChanges();
    const blog = ((fixture.nativeElement as HTMLElement).querySelector('.dot') as HTMLElement).style.background;
    fixture.componentRef.setInput('platform', Platform.Reddit);
    fixture.detectChanges();
    const reddit = ((fixture.nativeElement as HTMLElement).querySelector('.dot') as HTMLElement).style.background;
    expect(blog).not.toBe(reddit);
  });
});
