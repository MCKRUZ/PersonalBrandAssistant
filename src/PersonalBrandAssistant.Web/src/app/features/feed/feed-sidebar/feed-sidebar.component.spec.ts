import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FeedSidebarComponent } from './feed-sidebar.component';
import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { FeedHubService } from '../services/feed-hub.service';
import { EMPTY } from 'rxjs';

@Component({
  standalone: true,
  imports: [FeedSidebarComponent],
  template: `<app-feed-sidebar />`,
})
class TestHostComponent {}

describe('FeedSidebarComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: FeedHubService,
          useValue: {
            feedItemReceived$: EMPTY,
            summaryUpdated$: EMPTY,
            start: () => Promise.resolve(),
            stop: () => Promise.resolve(),
          },
        },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TestHostComponent);
    fixture.detectChanges();
  });

  it('should render QuickComposeWidget', () => {
    const widget = fixture.nativeElement.querySelector('app-quick-compose-widget');
    expect(widget).toBeTruthy();
  });

  it('should render TrendingTopicsWidget', () => {
    const widget = fixture.nativeElement.querySelector('app-trending-topics-widget');
    expect(widget).toBeTruthy();
  });
});
