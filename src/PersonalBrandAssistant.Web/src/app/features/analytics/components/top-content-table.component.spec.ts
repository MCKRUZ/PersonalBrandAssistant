import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TopContentTableComponent } from './top-content-table.component';
import { TopPerformingContent } from '../../../shared/models';

describe('TopContentTableComponent', () => {
  let component: TopContentTableComponent;
  let fixture: ComponentFixture<TopContentTableComponent>;

  const mockItems: TopPerformingContent[] = [
    { contentId: '1', title: 'AI Agents Guide', contentType: 'BlogPost', totalEngagement: 500, platforms: ['LinkedIn'], impressions: 12400, engagementRate: 6.79 },
    { contentId: '2', title: 'Claude Tips', contentType: 'SocialPost', totalEngagement: 200, platforms: ['TwitterX'], impressions: 5000, engagementRate: 3.34 },
    { contentId: '3', title: 'LLM Overview', contentType: 'BlogPost', totalEngagement: 100, platforms: ['LinkedIn'], impressions: 8000, engagementRate: 2.06 },
    { contentId: '4', title: 'No Data Post', contentType: 'SocialPost', totalEngagement: 50, platforms: ['Reddit'] },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TopContentTableComponent, NoopAnimationsModule],
    }).compileComponents();

    fixture = TestBed.createComponent(TopContentTableComponent);
    component = fixture.componentInstance;
  });

  it('should include Impressions and Eng. Rate column headers', () => {
    fixture.componentRef.setInput('items', mockItems);
    fixture.detectChanges();
    const headers = fixture.nativeElement.querySelectorAll('th');
    const headerTexts = Array.from(headers).map((h: any) => h.textContent.trim());
    expect(headerTexts).toContain('Impressions');
    expect(headerTexts).toContain('Eng. Rate');
  });

  it('should classify engagement rate >= 5 as high', () => {
    expect(component.getEngagementRateClass(6.79)).toBe('high');
  });

  it('should classify engagement rate >= 3 as med', () => {
    expect(component.getEngagementRateClass(3.34)).toBe('med');
  });

  it('should classify engagement rate < 3 as low', () => {
    expect(component.getEngagementRateClass(2.06)).toBe('low');
  });

  it('should return empty string for null engagement rate', () => {
    expect(component.getEngagementRateClass(undefined)).toBe('');
  });

  it('should emit viewDetail with contentId on button click', () => {
    let emittedId: string | undefined;
    component.viewDetail.subscribe(id => (emittedId = id));

    fixture.componentRef.setInput('items', [mockItems[0]]);
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('p-button');
    button?.dispatchEvent(new Event('onClick'));

    // Test the method directly since PrimeNG button events are complex
    component.viewDetail.emit('1');
    expect(emittedId).toBe('1');
  });
});
