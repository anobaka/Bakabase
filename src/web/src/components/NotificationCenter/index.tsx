"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { BellOutlined } from "@ant-design/icons";
import { Drawer, DrawerBody, DrawerContent, DrawerFooter, DrawerHeader } from "@heroui/react";

import NotificationItem from "./NotificationItem";
import { groupByDate } from "./groupByDate";

import { Badge, Button, Spinner } from "@/components/bakaui";
import { useNotificationsStore } from "@/stores/notifications";

const OptIconStyle = { fontSize: 20 };

const NotificationCenter: React.FC = () => {
  const { t } = useTranslation();
  const [isOpen, setIsOpen] = useState(false);

  const unreadCount = useNotificationsStore((s) => s.unreadCount);
  const items = useNotificationsStore((s) => s.items);
  const loaded = useNotificationsStore((s) => s.loaded);
  const loading = useNotificationsStore((s) => s.loading);
  const loadUnreadCount = useNotificationsStore((s) => s.loadUnreadCount);
  const loadFirstPage = useNotificationsStore((s) => s.loadFirstPage);
  const markAllAsRead = useNotificationsStore((s) => s.markAllAsRead);
  const clearRead = useNotificationsStore((s) => s.clearRead);

  // Initial badge count on mount.
  useEffect(() => {
    void loadUnreadCount();
  }, [loadUnreadCount]);

  const handleOpen = () => {
    setIsOpen(true);
    // Load list lazily on first open, then mark everything visible as read.
    if (!loaded) {
      void loadFirstPage().then(() => {
        if (useNotificationsStore.getState().unreadCount > 0) {
          void markAllAsRead();
        }
      });
    } else if (unreadCount > 0) {
      void markAllAsRead();
    }
  };

  const groups = groupByDate(items);

  return (
    <>
      <Badge color="danger" content={unreadCount} isInvisible={unreadCount === 0} size="sm">
        <Button
          isIconOnly
          color="default"
          title={t<string>("notificationCenter.openTooltip")}
          variant="light"
          onPress={handleOpen}
        >
          <BellOutlined style={OptIconStyle} />
        </Button>
      </Badge>

      <Drawer isOpen={isOpen} placement="right" size="md" onClose={() => setIsOpen(false)}>
        <DrawerContent>
          <DrawerHeader className="flex flex-col gap-1">
            {t("notificationCenter.title")}
          </DrawerHeader>
          <DrawerBody>
            {loading && !loaded ? (
              <div className="flex justify-center items-center h-full">
                <Spinner size="lg" />
              </div>
            ) : items.length === 0 ? (
              <div className="flex justify-center items-center h-full text-default-500">
                {t("notificationCenter.empty")}
              </div>
            ) : (
              <div className="flex flex-col gap-4">
                {groups.map((g) => (
                  <div key={g.key} className="flex flex-col gap-1">
                    <div className="text-xs text-default-500 px-1">
                      {t(`notificationCenter.group.${g.key}`)}
                    </div>
                    <div className="flex flex-col gap-0.5">
                      {g.items.map((n) => (
                        <NotificationItem key={n.id} notification={n} />
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </DrawerBody>
          {items.length > 0 && (
            <DrawerFooter className="flex gap-2 justify-end">
              <Button size="sm" variant="light" onPress={() => void clearRead()}>
                {t("notificationCenter.clearRead")}
              </Button>
            </DrawerFooter>
          )}
        </DrawerContent>
      </Drawer>
    </>
  );
};

export default NotificationCenter;
