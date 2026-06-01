"use client";

import React, { useState } from "react";

import BApi from "@/sdk/BApi";
import { Button, Input, toast } from "@/components/bakaui";
import { AppNotificationSeverity } from "@/sdk/constants";

const NotificationTest: React.FC = () => {
  const [title, setTitle] = useState("Test notification");
  const [body, setBody] = useState("Click the bell — this row should appear in the center.");

  const fire = async (severity: AppNotificationSeverity) => {
    try {
      // No explicit "Sent" toast — the SignalR-pushed notification toast already
      // confirms the request worked end-to-end, and two stacked toasts hide each
      // other behind HeroUI's scaling layout.
      await BApi.notification.createTestNotification({ title, body, severity });
    } catch (e: any) {
      toast.danger(e?.message ?? String(e));
    }
  };

  return (
    <div className="flex flex-col gap-3 max-w-xl p-2">
      <h3 className="text-lg font-semibold">Persistent notification</h3>
      <p className="text-sm text-default-500">
        Fires POST /notification/test. The backend persists it and pushes
        OnPersistentNotification — you should see a toast, the bell badge bump,
        and a new row inside the notification center.
      </p>
      <Input label="Title" value={title} onValueChange={setTitle} />
      <Input label="Body" value={body} onValueChange={setBody} />
      <div className="flex flex-wrap gap-2">
        <Button color="default" onPress={() => fire(AppNotificationSeverity.Info)}>
          Info
        </Button>
        <Button color="success" onPress={() => fire(AppNotificationSeverity.Success)}>
          Success
        </Button>
        <Button color="warning" onPress={() => fire(AppNotificationSeverity.Warning)}>
          Warning
        </Button>
        <Button color="danger" onPress={() => fire(AppNotificationSeverity.Error)}>
          Error
        </Button>
      </div>
    </div>
  );
};

export default NotificationTest;
