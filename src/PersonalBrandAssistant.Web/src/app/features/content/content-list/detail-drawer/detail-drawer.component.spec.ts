import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { DetailDrawerComponent } from './detail-drawer.component';
import { ContentStore } from '../../stores/content.store';
import { ContentService } from '../../services/content.service';
import { ContentStatus, ContentType, Platform } from '../../models/content.model';
import type { Content, ContentDetail } from '../../models/content.model';
import type { PagedResult } from '../../../../models/pagination.model';

function makeContent(over: Partial<Content> = {}): Content {
  return {
    id: 'c1',
    title: 'Drawer title',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: 70,
    tags: ['angular'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    ...over,
  };
}

function page(items: Content[]): PagedResult<Content> {
  return { items, totalCount: items.length, page: 1, pageSize: 1000, totalPages: 1 };
}

function detail(over: Partial<ContentDetail> = {}): ContentDetail {
  return {
    ...makeContent(),
    body: 'The full body text.',
    viralityPrediction: null,
    sourceIdeaId: null,
    parentContentId: null,
    children: [],
    ...over,
  } as ContentDetail;
}

describe('DetailDrawerComponent', () => {
  let fixture: ComponentFixture<DetailDrawerComponent>;
  let component: DetailDrawerComponent;
  let store: InstanceType<typeof ContentStore>;
  let svc: jasmine.SpyObj<ContentService>;
  let router: Router;

  beforeEach(() => {
    svc = jasmine.createSpyObj('ContentService', [
      'list', 'get', 'schedule', 'draft', 'approve', 'submitForReview',
      'requestChanges', 'unschedule', 'publish', 'unpublish', 'restore',
    ]);
    svc.list.and.returnValue(of(page([])));
    svc.get.and.returnValue(of(detail()));
    ['schedule', 'draft', 'approve', 'submitForReview', 'requestChanges', 'unschedule', 'publish', 'unpublish', 'restore'].forEach(
      (m) => (svc as any)[m].and.returnValue(of(void 0))
    );

    TestBed.configureTestingModule({
      imports: [DetailDrawerComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        ContentStore,
        { provide: ContentService, useValue: svc },
      ],
    });

    fixture = TestBed.createComponent(DetailDrawerComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(ContentStore);
    router = TestBed.inject(Router);
  });

  function seed(items: Content[]): void {
    svc.list.and.returnValue(of(page(items)));
    store.loadAll();
  }

  it('is closed when contentId is null', () => {
    fixture.componentRef.setInput('contentId', null);
    fixture.detectChanges();
    expect(component.open()).toBeFalse();
  });

  it('resolves the content from the store by id', () => {
    seed([makeContent({ id: 'c1' })]);
    fixture.componentRef.setInput('contentId', 'c1');
    fixture.detectChanges();
    expect(component.open()).toBeTrue();
    expect(component.content()?.title).toBe('Drawer title');
  });

  it('"Open in editor" navigates to /content/:id', () => {
    seed([makeContent({ id: 'c1' })]);
    fixture.componentRef.setInput('contentId', 'c1');
    fixture.detectChanges();
    spyOn(router, 'navigate');
    component.openEditor('c1');
    expect(router.navigate).toHaveBeenCalledWith(['/content', 'c1']);
  });

  it('shows Publish for Approved/Scheduled, else Move to {next}', () => {
    expect(component.isPublishable(ContentStatus.Approved)).toBeTrue();
    expect(component.isPublishable(ContentStatus.Scheduled)).toBeTrue();
    expect(component.isPublishable(ContentStatus.Draft)).toBeFalse();
    expect(component.next(ContentStatus.Draft)).toBe(ContentStatus.Review);
  });

  it('Move-to action calls store.transition for a normal next status', () => {
    seed([makeContent({ id: 'c1', status: ContentStatus.Draft })]);
    fixture.componentRef.setInput('contentId', 'c1');
    fixture.detectChanges();
    spyOn(store, 'transition');
    component.onContextAction(makeContent({ id: 'c1', status: ContentStatus.Draft }), ContentStatus.Review);
    expect(store.transition).toHaveBeenCalledWith('c1', ContentStatus.Review);
  });

  it('Move-to Scheduled opens the schedule dialog instead of transitioning', () => {
    seed([makeContent({ id: 'c1', status: ContentStatus.Approved })]);
    fixture.componentRef.setInput('contentId', 'c1');
    fixture.detectChanges();
    spyOn(store, 'transition');
    component.onContextAction(makeContent({ id: 'c1', status: ContentStatus.Approved }), ContentStatus.Scheduled);
    expect(store.transition).not.toHaveBeenCalled();
    expect(component.scheduleVisible()).toBeTrue();

    component.onScheduleConfirm('2026-07-01T10:00:00.000Z');
    expect(svc.schedule).toHaveBeenCalledWith('c1', { scheduledAt: '2026-07-01T10:00:00.000Z' });
  });

  it('emits closed when the drawer is dismissed', () => {
    spyOn(component.closed, 'emit');
    component.onVisibleChange(false);
    expect(component.closed.emit).toHaveBeenCalled();
  });
});
