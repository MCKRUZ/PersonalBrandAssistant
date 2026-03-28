import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlatformHealthCardsComponent } from './platform-health-cards.component';
import { PlatformSummary } from '../models/dashboard.model';

describe('PlatformHealthCardsComponent', () => {
  let component: PlatformHealthCardsComponent;
  let fixture: ComponentFixture<PlatformHealthCardsComponent>;

  const mockPlatforms: readonly PlatformSummary[] = [
    { platform: 'TwitterX', followerCount: 2841, postCount: 45, avgEngagement: 128, topPostTitle: 'Why agent frameworks need a rethink', topPostUrl: 'https://x.com/post/1', isAvailable: true },
    { platform: 'LinkedIn', followerCount: 5200, postCount: 0, avgEngagement: 0, topPostTitle: null, topPostUrl: null, isAvailable: false },
    { platform: 'YouTube', followerCount: 1200, postCount: 18, avgEngagement: 3400, topPostTitle: 'Building AI Agents', topPostUrl: 'https://youtube.com/watch?v=1', isAvailable: true },
    { platform: 'Instagram', followerCount: 890, postCount: 32, avgEngagement: 95, topPostTitle: 'AI at EY', topPostUrl: 'https://instagram.com/p/1', isAvailable: true },
    { platform: 'Reddit', followerCount: null, postCount: 12, avgEngagement: 42, topPostTitle: 'Claude Code tips', topPostUrl: 'https://reddit.com/r/1', isAvailable: true },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlatformHealthCardsComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(PlatformHealthCardsComponent);
    component = fixture.componentInstance;
  });

  it('should render a card for each platform in the input array', () => {
    fixture.componentRef.setInput('platforms', mockPlatforms);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
    expect(cards.length).toBe(5);
  });

  it('should display platform brand color as the top border', () => {
    fixture.componentRef.setInput('platforms', mockPlatforms);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
    const twitterCard = cards[0] as HTMLElement;
    expect(twitterCard.style.getPropertyValue('--platform-color')).toBe('#1DA1F2');
  });

  it('should show follower count when available', () => {
    fixture.componentRef.setInput('platforms', mockPlatforms);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
    const twitterCard = cards[0] as HTMLElement;
    expect(twitterCard.textContent).toContain('2,841');
  });

  it('should show N/A when followerCount is null', () => {
    fixture.componentRef.setInput('platforms', mockPlatforms);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
    const redditCard = cards[4] as HTMLElement;
    expect(redditCard.textContent).toContain('N/A');
  });

  it('should show post count and average engagement', () => {
    fixture.componentRef.setInput('platforms', mockPlatforms);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
    const twitterCard = cards[0] as HTMLElement;
    expect(twitterCard.textContent).toContain('45');
    expect(twitterCard.textContent).toContain('128');
  });

  it('should display top post title when present', () => {
    fixture.componentRef.setInput('platforms', mockPlatforms);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
    const twitterCard = cards[0] as HTMLElement;
    expect(twitterCard.textContent).toContain('Why agent frameworks need a rethink');
  });

  it('should show Coming Soon badge for LinkedIn', () => {
    fixture.componentRef.setInput('platforms', mockPlatforms);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.platform-card');
    const linkedinCard = cards[1] as HTMLElement;
    expect(linkedinCard.textContent).toContain('Coming Soon');
    expect(linkedinCard.querySelector('.unavailable-overlay')).toBeTruthy();
  });

  it('should show Data unavailable for non-LinkedIn unavailable platforms', () => {
    const unavailableReddit: PlatformSummary = { platform: 'Reddit', followerCount: null, postCount: 0, avgEngagement: 0, topPostTitle: null, topPostUrl: null, isAvailable: false };
    fixture.componentRef.setInput('platforms', [unavailableReddit]);
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('.platform-card') as HTMLElement;
    expect(card.textContent).toContain('Data unavailable');
  });
});
