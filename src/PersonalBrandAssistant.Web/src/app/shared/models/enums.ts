export type ContentStatus = 'Draft' | 'Review' | 'Approved' | 'Scheduled' | 'Publishing' | 'Published' | 'Failed' | 'Archived';

export type ContentType = 'BlogPost' | 'SocialPost' | 'Thread' | 'VideoDescription';

export type PlatformType = 'TwitterX' | 'LinkedIn' | 'Instagram' | 'YouTube' | 'Reddit';

export type AutonomyLevel = 'Manual' | 'Assisted' | 'SemiAuto' | 'Autonomous';

export type CalendarSlotStatus = 'Open' | 'Filled' | 'Published' | 'Skipped';

export type TrendSuggestionStatus = 'Pending' | 'Accepted' | 'Dismissed';

export type AgentCapabilityType = 'Writer' | 'Social' | 'Repurpose' | 'Engagement' | 'Analytics';

export type AgentExecutionStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

export type ActorType = 'User' | 'System' | 'Agent';

export type ContentTrigger = 'Submit' | 'Approve' | 'Reject' | 'Schedule' | 'Unschedule' | 'Publish' | 'Complete' | 'Fail' | 'ReturnToDraft' | 'Archive' | 'Unarchive' | 'Requeue' | 'Retry';

export type ModelTier = 'Fast' | 'Standard' | 'Advanced';

export type NotificationType = 'ContentReadyForReview' | 'ContentApproved' | 'ContentRejected' | 'ContentPublished' | 'ContentFailed' | 'PlatformDisconnected' | 'PlatformTokenExpiring' | 'PlatformScopeMismatch';

export type PlatformPublishStatus = 'Pending' | 'Published' | 'Failed' | 'RateLimited' | 'Skipped' | 'Processing';

export type TrendSourceType = 'TrendRadar' | 'FreshRSS' | 'Reddit' | 'HackerNews' | 'YouTube' | 'Email' | 'BrowserHistory' | 'RssFeed';
