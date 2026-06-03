import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PipelineBarComponent } from './pipeline-bar.component';
import { ContentStore } from '../../stores/content.store';
import { ContentService } from '../../services/content.service';
import { ContentStatus, ContentType, Platform } from '../../models/content.model';
import type { Content } from '../../models/content.model';
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

function page(items: Content[]): PagedResult<Content> {
  return { items, totalCount: items.length, page: 1, pageSize: 1000, totalPages: 1 };
}

describe('PipelineBarComponent', () => {
  let fixture: ComponentFixture<PipelineBarComponent>;
  let store: InstanceType<typeof ContentStore>;
  let svc: jasmine.SpyObj<ContentService>;

  beforeEach(() => {
    svc = jasmine.createSpyObj('ContentService', ['list']);
    svc.list.and.returnValue(of(page([])));

    TestBed.configureTestingModule({
      imports: [PipelineBarComponent],
      providers: [ContentStore, { provide: ContentService, useValue: svc }],
    });

    fixture = TestBed.createComponent(PipelineBarComponent);
    store = TestBed.inject(ContentStore);
  });

  function seed(items: Content[]): void {
    svc.list.and.returnValue(of(page(items)));
    store.loadAll();
    fixture.detectChanges();
  }

  it('renders an "All {total}" pill plus one pill per status with its count', () => {
    seed([
      makeContent({ id: 'a', status: ContentStatus.Draft }),
      makeContent({ id: 'b', status: ContentStatus.Draft }),
      makeContent({ id: 'c', status: ContentStatus.Published }),
    ]);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="pill-all"]')?.textContent).toContain('3');
    expect(el.querySelector('[data-testid="pill-Draft"] .count')?.textContent).toContain('2');
    expect(el.querySelector('[data-testid="pill-Published"] .count')?.textContent).toContain('1');
    // 1 All + 7 statuses
    expect(el.querySelectorAll('.pill').length).toBe(8);
  });

  it('clicking a status pill sets activeStatus; re-click clears it', () => {
    seed([makeContent({ status: ContentStatus.Review })]);
    const pill = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="pill-Review"]'
    ) as HTMLButtonElement;
    pill.click();
    expect(store.activeStatus()).toBe(ContentStatus.Review);
    pill.click();
    expect(store.activeStatus()).toBeNull();
  });

  it('zero-count pills get the empty (dimmed) class; selected pill gets the on class', () => {
    seed([makeContent({ status: ContentStatus.Draft })]);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="pill-Idea"]')?.classList.contains('empty')).toBeTrue();

    store.setActiveStatus(ContentStatus.Draft);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="pill-Draft"]')?.classList.contains('on')).toBeTrue();
  });
});
