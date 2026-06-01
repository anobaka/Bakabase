import { create } from "zustand";

import BApi from "@/sdk/BApi";
import type { components } from "@/sdk/BApi2";

export type NotificationViewModel =
  components["schemas"]["Bakabase.Modules.Notification.Abstractions.Models.View.NotificationViewModel"];

interface NotificationsState {
  unreadCount: number;
  items: NotificationViewModel[];
  loaded: boolean;
  loading: boolean;
  totalCount: number;
  pageIndex: number;
  pageSize: number;

  loadUnreadCount: () => Promise<void>;
  loadFirstPage: () => Promise<void>;
  loadPage: (pageIndex: number) => Promise<void>;
  markAsRead: (ids: number[]) => Promise<void>;
  markAllAsRead: () => Promise<void>;
  clearRead: () => Promise<void>;
  deleteByIds: (ids: number[]) => Promise<void>;

  // SignalR push entry point
  appendIncoming: (n: NotificationViewModel) => void;
}

const PAGE_SIZE = 30;

export const useNotificationsStore = create<NotificationsState>((set, get) => ({
  unreadCount: 0,
  items: [],
  loaded: false,
  loading: false,
  totalCount: 0,
  pageIndex: 1,
  pageSize: PAGE_SIZE,

  loadUnreadCount: async () => {
    const rsp = await BApi.notification.getUnreadNotificationCount();
    set({ unreadCount: rsp.data ?? 0 });
  },

  loadFirstPage: async () => {
    await get().loadPage(1);
  },

  loadPage: async (pageIndex: number) => {
    set({ loading: true });
    try {
      const rsp = await BApi.notification.searchNotifications({
        pageIndex,
        pageSize: PAGE_SIZE,
      });
      set({
        items: (rsp.data ?? []) as NotificationViewModel[],
        totalCount: rsp.totalCount ?? 0,
        pageIndex,
        loaded: true,
      });
    } finally {
      set({ loading: false });
    }
  },

  markAsRead: async (ids: number[]) => {
    if (ids.length === 0) return;
    await BApi.notification.markNotificationsAsRead({ ids });
    const now = new Date().toISOString();
    set((s) => ({
      items: s.items.map((n) => (ids.includes(n.id) && !n.readAt ? { ...n, readAt: now } : n)),
      unreadCount: Math.max(0, s.unreadCount - ids.length),
    }));
  },

  markAllAsRead: async () => {
    await BApi.notification.markNotificationsAsRead({});
    const now = new Date().toISOString();
    set((s) => ({
      items: s.items.map((n) => (n.readAt ? n : { ...n, readAt: now })),
      unreadCount: 0,
    }));
  },

  clearRead: async () => {
    await BApi.notification.clearReadNotifications();
    set((s) => {
      const readCount = s.items.filter((n) => n.readAt).length;
      return {
        items: s.items.filter((n) => !n.readAt),
        totalCount: Math.max(0, s.totalCount - readCount),
      };
    });
  },

  deleteByIds: async (ids: number[]) => {
    if (ids.length === 0) return;
    await BApi.notification.deleteNotifications({ ids });
    set((s) => ({
      items: s.items.filter((n) => !ids.includes(n.id)),
      totalCount: Math.max(0, s.totalCount - ids.length),
      unreadCount: Math.max(
        0,
        s.unreadCount - s.items.filter((n) => ids.includes(n.id) && !n.readAt).length,
      ),
    }));
  },

  appendIncoming: (n: NotificationViewModel) => {
    set((s) => ({
      items: [n, ...s.items],
      totalCount: s.totalCount + 1,
      unreadCount: s.unreadCount + (n.readAt ? 0 : 1),
    }));
  },
}));
