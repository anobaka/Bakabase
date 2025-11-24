export enum AppNotificationSeverity {
  Info = 0,
  Success = 1,
  Warning = 2,
  Error = 3,
}

export enum AppNotificationBehavior {
  AutoDismiss = 0,
  Persistent = 1,
}

export interface AppNotificationMessageViewModel {
  id: string;
  title: string;
  message?: string;
  severity: AppNotificationSeverity;
  behavior: AppNotificationBehavior;
  durationMs?: number;
  createdAt: string;
  metadata?: Record<string, any>;
}
