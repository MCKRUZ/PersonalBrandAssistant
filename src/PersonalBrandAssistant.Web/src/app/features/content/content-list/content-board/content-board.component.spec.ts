import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import type { CdkDrag, CdkDragDrop, CdkDropList } from '@angular/cdk/drag-drop';
import { ContentBoardComponent } from './content-board.component';
import { ContentStore } from '../../stores/content.store';
import { ContentService } from '../../services/content.service';
import { ContentStatus, ContentType, Platform } from '../../models/content.model';
import type { Content, ContentDetail } from '../../models/content.model';
import type { PagedResult } from '../../../../models/pagination.model';

function makeContent(over: Partial<Content> = {}): Content {
  return {
    id: 'c1',
    title: 'Hello',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: null,
    tags: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    ...over,
  };
}

function detail(over: Partial<ContentDetail> = {}): ContentDetail {
  return {
    ...makeContent(),
    body: 'body',
    viralityPrediction: null,
    sourceIdeaId: null,
    parentContentId: null,
    children: [],
    ...over,
  } as ContentDetail;
}

function page(items: Content[]): PagedResult<Content> {
  return { items, totalCount: items.length, page: 1, pageSize: 1000, totalPages: 1 };
}

/** Build a minimal CdkDragDrop the handler reads: previousContainer, container.id, item.data. */
function dropEvent(
  card: Content,
  targetStatus: ContentStatus,
  sameContainer = false
): CdkDragDrop<Content[]> {
  const container = { id: targetStatus } as unknown as CdkDropList<Content[]>;
  const previousContainer = (sameContainer
    ? container
    : ({ id: card.status } as unknown as CdkDropList<Content[]>));
  return {
    previousContainer,
    container,
    item: { data: card } as unknown as CdkDrag<Content>,
    previousIndex: 0,
    currentIndex: 0,
    isPointerOverContainer: true,
    distance: { x: 0, y: 0 },
    dropPoint: { x: 0, y: 0 },
    event: {} as MouseEvent,
  } as unknown as CdkDragDrop<Content[]>;
}

describe('ContentBoardComponent', () => {
  let fixture: ComponentFixture<ContentBoardComponent>;
  let component: ContentBoardComponent;
  let store: InstanceType<typeof ContentStore>;
  let svc: jasmine.SpyObj<ContentService>;

  beforeEach(() => {
    svc = jasmine.createSpyObj('ContentService', [
      'list', 'get', 'draft', 'approve', 'submitForReview', 'requestChanges',
      'schedule', 'unschedule', 'publish', 'unpublish', 'restore',
    ]);
    svc.list.and.returnValue(of(page([])));
    svc.get.and.returnValue(of(detail()));
    ['draft', 'approve', 'submitForReview', 'requestChanges', 'unschedule', 'publish', 'unpublish', 'restore', 'schedule'].forEach(
      (m) => (svc as any)[m].and.returnValue(of(void 0))
    );

    TestBed.configureTestingModule({
      imports: [ContentBoardComponent],
      providers: [ContentStore, { provide: ContentService, useValue: svc }],
    });

    fixture = TestBed.createComponent(ContentBoardComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(ContentStore);
  });

  function seed(items: Content[]): void {
    svc.list.and.returnValue(of(page(items)));
    store.loadAll();
    fixture.detectChanges();
  }

  it('renders one drop-list column per status', () => {
    seed([]);
    const cols = (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid^="col-"]');
    expect(cols.length).toBe(7);
  });

  it('canDropInto returns true only for legal target statuses', () => {
    const draftCard = makeContent({ status: ContentStatus.Draft });
    const drag = { data: draftCard } as unknown as CdkDrag<Content>;
    // Draft -> Review, Approved legal; Draft -> Published illegal
    expect(component.canDropInto(ContentStatus.Review)(drag)).toBeTrue();
    expect(component.canDropInto(ContentStatus.Approved)(drag)).toBeTrue();
    expect(component.canDropInto(ContentStatus.Published)(drag)).toBeFalse();
    // staying put allowed
    expect(component.canDropInto(ContentStatus.Draft)(drag)).toBeTrue();
  });

  it('onDrop with same container is a no-op', () => {
    const card = makeContent({ status: ContentStatus.Draft });
    seed([card]);
    spyOn(store, 'transition');
    component.onDrop(dropEvent(card, ContentStatus.Draft, true));
    expect(store.transition).not.toHaveBeenCalled();
  });

  it('onDrop legal cross-column calls store.transition with the target status', () => {
    const card = makeContent({ id: 'x', status: ContentStatus.Draft });
    seed([card]);
    spyOn(store, 'transition');
    component.onDrop(dropEvent(card, ContentStatus.Approved));
    expect(store.transition).toHaveBeenCalledWith('x', ContentStatus.Approved);
  });

  it('onDrop into Scheduled opens the schedule dialog and defers to ContentService.schedule', () => {
    const card = makeContent({ id: 'y', status: ContentStatus.Approved });
    seed([card]);
    spyOn(store, 'transition');

    component.onDrop(dropEvent(card, ContentStatus.Scheduled));
    expect(store.transition).not.toHaveBeenCalled();
    expect(svc.schedule).not.toHaveBeenCalled();
    expect(component.scheduleVisible()).toBeTrue();

    component.onScheduleConfirm('2026-07-01T10:00:00.000Z');
    expect(svc.schedule).toHaveBeenCalledWith('y', { scheduledAt: '2026-07-01T10:00:00.000Z' });
    expect(component.scheduleVisible()).toBeFalse();
  });

  it('renders a dashed "Drop here" target for empty columns', () => {
    seed([makeContent({ status: ContentStatus.Draft })]);
    const dropHints = (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="drop-here"]');
    // Every empty column shows one; Draft has a card so it does not.
    expect(dropHints.length).toBe(6);
  });

  it('schedule cancel closes the dialog without scheduling', () => {
    const card = makeContent({ status: ContentStatus.Approved });
    seed([card]);
    component.onDrop(dropEvent(card, ContentStatus.Scheduled));
    component.onScheduleCancel();
    expect(component.scheduleVisible()).toBeFalse();
    expect(svc.schedule).not.toHaveBeenCalled();
  });
});
