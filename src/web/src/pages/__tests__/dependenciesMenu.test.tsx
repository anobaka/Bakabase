import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { HashRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { routesMenuConfig } from "@/components/routesMenuConfig";
import {
  dependencyManagerPath,
  standaloneDependencyRedirect,
} from "@/components/Dependency/routes";
import DependenciesRedirect from "@/components/Dependency/DependenciesRedirect";
import { extractMenu } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";
import { mergeNavigationLayout } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/navigationLayout";
import { deviceSwitchDestination } from "@/features/federation/deviceSwitchDestination";
import cn from "@/locales/cn/common.json";
import en from "@/locales/en/common.json";

vi.mock("@/sdk/BApi", () => {
  const anything = (): unknown =>
    new Proxy(() => Promise.resolve({ code: 0 }), {
      get: (_, key) => (key === "then" ? undefined : anything()),
    });
  return { default: anything() };
});

const originalUrl = window.location.href;
afterEach(() => {
  cleanup();
  window.history.replaceState({}, "", originalUrl);
});

const Location = () => {
  const value = useLocation();
  return <output>{value.pathname + value.search}</output>;
};

describe("dependency configuration navigation", () => {
  it("keeps dependencies inside configuration and retains a hidden standalone alias", () => {
    const system = routesMenuConfig.find((route) => route.name === "menu.system")!;
    expect(system.children!.some((route) => route.name === "menu.dependencies")).toBe(false);
    expect(system.children!.filter((route) => route.path === "/configuration")).toHaveLength(1);
    const alias = routesMenuConfig.filter((route) => route.path === "/dependencies");
    expect(alias).toHaveLength(1);
    expect(alias[0]).toMatchObject({ menu: false, layout: "basic" });
    expect(cn["menu.dependencies"]).toBe("依赖管理");
    expect(en["menu.dependencies"]).toBe("Dependencies");
    const catalog = extractMenu(routesMenuConfig);
    const previous = [...catalog, { name: "menu.dependencies", path: "/dependencies" }];
    const saved = mergeNavigationLayout(previous, undefined, (key) => key);
    for (const capabilities of [
      { isPureClient: false, isLocalNode: true },
      { isPureClient: false, isLocalNode: false },
      { isPureClient: true, isLocalNode: false },
    ])
      expect(
        deviceSwitchDestination(
          "/configuration?tab=dependency&component=local-only-id",
          catalog,
          saved,
          capabilities,
        ),
      ).toBe("/configuration");
  });

  it("keeps component targeting inside the hash router", () => {
    expect(dependencyManagerPath()).toBe("/configuration?tab=dependency");
    expect(dependencyManagerPath("tool/id & version")).toBe(
      "/configuration?tab=dependency&component=tool%2Fid+%26+version",
    );
    expect(dependencyManagerPath(undefined, "百度 网盘")).toBe(
      "/configuration?tab=dependency&q=%E7%99%BE%E5%BA%A6+%E7%BD%91%E7%9B%98",
    );
    expect(
      standaloneDependencyRedirect("?component=ffmpeg&q=media&filter=optional&tab=network"),
    ).toBe("/configuration?tab=dependency&component=ffmpeg&q=media&filter=optional");
  });

  it("redirects legacy hash bookmarks without consuming outer application parameters", async () => {
    window.history.replaceState(
      {},
      "",
      "/?desktopTicket=keep#/dependencies?component=ffmpeg&q=media&filter=optional",
    );
    render(
      <HashRouter>
        <Routes>
          <Route path="/configuration" element={<Location />} />
          <Route path="/dependencies" element={<DependenciesRedirect />} />
        </Routes>
      </HashRouter>,
    );
    await waitFor(() =>
      expect(
        screen.getByText("/configuration?tab=dependency&component=ffmpeg&q=media&filter=optional"),
      ).toBeInTheDocument(),
    );
    expect(window.location.search).toBe("?desktopTicket=keep");
    expect(window.location.hash).toBe(
      "#/configuration?tab=dependency&component=ffmpeg&q=media&filter=optional",
    );
  });
});
