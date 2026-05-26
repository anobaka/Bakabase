import { useState } from "react";

import {
  AppUpdateBannerView,
  type AppUpdateBannerViewState,
} from "@/layouts/BasicLayout/components/AppUpdateBanner";

interface Case {
  name: string;
  state: AppUpdateBannerViewState;
}

const cases: Case[] = [
  { name: "checking", state: { kind: "checking" } },
  {
    name: "downloading (indeterminate)",
    state: { kind: "downloading", version: "10.0.0" },
  },
  {
    name: "downloading (42%)",
    state: { kind: "downloading", version: "10.0.0", percentage: 42 },
  },
  { name: "pendingRestart", state: { kind: "pendingRestart" } },
  { name: "failed", state: { kind: "failed", error: undefined } },
  { name: "hidden", state: { kind: "hidden" } },
];

const AppUpdateBannerTest = () => {
  const [collapsed, setCollapsed] = useState(false);

  return (
    <div className="flex flex-col gap-4 p-2">
      <label className="flex items-center gap-2 text-sm">
        <input
          checked={collapsed}
          type="checkbox"
          onChange={(e) => setCollapsed(e.target.checked)}
        />
        collapsed (renders the icon-only variant used when the nav sidebar is folded)
      </label>

      <div className="flex flex-wrap gap-6">
        {cases.map((c) => (
          <div key={c.name} className="flex flex-col gap-2">
            <div className="text-xs text-foreground-500">{c.name}</div>
            <div
              className="rounded-md border border-dashed border-default-200 dark:border-default-100"
              style={{ width: collapsed ? 68 : 220 }}
            >
              <AppUpdateBannerView
                collapsed={collapsed}
                state={c.state}
                onRestart={() => console.log("restart")}
                onRetry={() => console.log("retry")}
              />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

export default AppUpdateBannerTest;
