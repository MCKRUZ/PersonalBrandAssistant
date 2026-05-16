import { RelativeTimePipe } from './relative-time.pipe';

describe('RelativeTimePipe', () => {
  let pipe: RelativeTimePipe;

  beforeEach(() => {
    pipe = new RelativeTimePipe();
  });

  it('should return empty string for null', () => {
    expect(pipe.transform(null)).toBe('');
  });

  it('should return "Just now" for times less than a minute ago', () => {
    const now = new Date().toISOString();
    expect(pipe.transform(now)).toBe('Just now');
  });

  it('should return minutes for times less than an hour ago', () => {
    const thirtyMinAgo = new Date(Date.now() - 30 * 60_000).toISOString();
    expect(pipe.transform(thirtyMinAgo)).toBe('30m ago');
  });

  it('should return hours for times less than a day ago', () => {
    const twoHoursAgo = new Date(Date.now() - 2 * 3_600_000).toISOString();
    expect(pipe.transform(twoHoursAgo)).toBe('2h ago');
  });

  it('should return days for times less than 30 days ago', () => {
    const fiveDaysAgo = new Date(Date.now() - 5 * 86_400_000).toISOString();
    expect(pipe.transform(fiveDaysAgo)).toBe('5d ago');
  });

  it('should return months for times 30+ days ago', () => {
    const sixtyDaysAgo = new Date(Date.now() - 60 * 86_400_000).toISOString();
    expect(pipe.transform(sixtyDaysAgo)).toBe('2mo ago');
  });

  it('should return "Just now" for future dates', () => {
    const future = new Date(Date.now() + 60_000).toISOString();
    expect(pipe.transform(future)).toBe('Just now');
  });
});
