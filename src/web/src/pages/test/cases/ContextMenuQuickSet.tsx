"use client";

import React from "react";

import QuickSetPropertyConfig from "@/components/Resource/components/QuickSetPropertyConfig";
import { useUiOptionsStore } from "@/stores/options";

/**
 * Test case for the "Quick Set Property" configuration component.
 * Connected to real UIOptions store so changes persist.
 */
const ContextMenuQuickSetTest = () => {
  const uiOptionsStore = useUiOptionsStore();
  const resourceOptions = uiOptionsStore.data?.resource;
  const items = resourceOptions?.customContextMenuItems ?? [];

  return (
    <div className={"p-4"} style={{ maxWidth: 800 }}>
      <QuickSetPropertyConfig
        autoAddRecentPropertyValues={resourceOptions?.autoAddRecentPropertyValues}
        items={items}
        onAutoAddChange={(value) =>
          uiOptionsStore.patch({
            resource: { ...resourceOptions, autoAddRecentPropertyValues: value },
          })
        }
        onChange={(newItems) =>
          uiOptionsStore.patch({
            resource: { ...resourceOptions, customContextMenuItems: newItems },
          })
        }
      />
    </div>
  );
};

ContextMenuQuickSetTest.displayName = "ContextMenuQuickSetTest";

export default ContextMenuQuickSetTest;
