import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FeedSidebarComponent } from './feed-sidebar.component';

describe('FeedSidebarComponent', () => {
  let fixture: ComponentFixture<FeedSidebarComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FeedSidebarComponent],
      schemas: [NO_ERRORS_SCHEMA],
    }).overrideComponent(FeedSidebarComponent, {
      set: { imports: [], schemas: [NO_ERRORS_SCHEMA] },
    });

    fixture = TestBed.createComponent(FeedSidebarComponent);
    fixture.detectChanges();
  });

  it('should render app-quick-compose-widget', () => {
    const el = fixture.nativeElement.querySelector('app-quick-compose-widget');
    expect(el).toBeTruthy();
  });

  it('should render app-trending-topics-widget', () => {
    const el = fixture.nativeElement.querySelector('app-trending-topics-widget');
    expect(el).toBeTruthy();
  });
});
