import { NotificationType } from './enums';

export interface Notification {
  readonly id: string;
  readonly userId: string;
  readonly type: NotificationType;
  readonly title: string;
  readonly message: string;
  readonly contentId?: string;
  readonly isRead: boolean;
  readonly createdAt: string;
}
