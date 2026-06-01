import React from "react";

import type { NotificationViewModel } from "@/stores/notifications";

import { AppNotificationSeverity } from "@/sdk/constants";

interface IProps {
  notification: NotificationViewModel;
  onClick?: () => void;
}

const severityColor: Record<AppNotificationSeverity, string> = {
  [AppNotificationSeverity.Info]: "bg-default-300",
  [AppNotificationSeverity.Success]: "bg-success",
  [AppNotificationSeverity.Warning]: "bg-warning",
  [AppNotificationSeverity.Error]: "bg-danger",
};

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

const NotificationItem: React.FC<IProps> = ({ notification, onClick }) => {
  const isUnread = !notification.readAt;
  const dot =
    severityColor[notification.severity as AppNotificationSeverity] ?? "bg-default-300";

  return (
    <button
      className={`text-left w-full px-3 py-2 rounded-md hover:bg-default-100 transition-colors flex gap-2 ${
        isUnread ? "bg-default-50" : "opacity-70"
      }`}
      type="button"
      onClick={onClick}
    >
      <span className={`mt-1 inline-block w-2 h-2 rounded-full flex-shrink-0 ${dot}`} />
      <span className="flex-1 min-w-0">
        <span className="block font-medium truncate">{notification.title}</span>
        {notification.body && (
          <span className="block text-sm text-default-500 line-clamp-2">
            {notification.body}
          </span>
        )}
        <span className="block text-xs text-default-400 mt-1">
          {formatTime(notification.createdAt)}
        </span>
      </span>
    </button>
  );
};

export default NotificationItem;
