import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FeedNewItemsBannerComponent } from './feed-new-items-banner.component';
import { FeedStore } from '../store/feed.store';
import { signal } from '@angular/core';

describe('FeedNewItemsBannerComponent', () => {
  let fixture: ComponentFixture<FeedNewItemsBannerComponent>;
  let newItemCount: ReturnType<typeof signal<number>>;
  let loadNewItemsSpy: jasmine.Spy;

  beforeEach(async () => {
    newItemCount = signal(0);
    loadNewItemsSpy = jasmine.createSpy('loadNewItems');

    await TestBed.configureTestingModule({
      imports: [FeedNewItemsBannerComponent],
      providers: [
        {
          provide: FeedStore,
          useValue: {
            newItemCount,
            loadNewItems: loadNewItemsSpy,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(FeedNewItemsBannerComponent);
    fixture.detectChanges();
  });

  it('should be visible when store.newItemCount > 0', () => {
    newItemCount.set(3);
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="new-items-banner"]');
    expect(banner).toBeTruthy();
  });

  it('should be hidden when store.newItemCount is 0', () => {
    newItemCount.set(0);
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="new-items-banner"]');
    expect(banner).toBeNull();
  });

  it('should display singular message for count of 1', () => {
    newItemCount.set(1);
    fixture.detectChanges();

    const message = fixture.nativeElement.querySelector('[data-testid="banner-message"]');
    expect(message?.textContent).toContain('1 new item');
    expect(message?.textContent).not.toContain('items');
  });

  it('should display plural message for count > 1', () => {
    newItemCount.set(5);
    fixture.detectChanges();

    const message = fixture.nativeElement.querySelector('[data-testid="banner-message"]');
    expect(message?.textContent).toContain('5 new items');
  });

  it('should call store.loadNewItems when Show button clicked', () => {
    newItemCount.set(3);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('[data-testid="show-btn"]') as HTMLButtonElement;
    btn.click();

    expect(loadNewItemsSpy).toHaveBeenCalled();
  });

  it('should have slide-down animation class', () => {
    newItemCount.set(1);
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="new-items-banner"]');
    expect(banner?.classList.contains('slide-down')).toBeTrue();
  });
});
