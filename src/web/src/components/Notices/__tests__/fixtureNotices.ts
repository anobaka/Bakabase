import type { NoticeDefinition } from "../registry";

import { AiOutlineSwap } from "react-icons/ai";

/**
 * A second notice for the startup and help-center tests, which need two to page between and
 * one whose button goes to a page. The app ships one notice today; these tests are about how
 * notices are shown, not about which ones ship (the registry test covers those).
 */
export const fixtureRouteNotice: NoticeDefinition = {
  id: "fixture-route-notice",
  introducedIn: "2.4.0",
  order: 20,
  icon: AiOutlineSwap,
  titleKey: "notices.item.fixture.title",
  bodyKey: "notices.item.fixture.body",
  pointKeys: ["notices.item.fixture.point"],
  action: {
    kind: "route",
    labelKey: "notices.item.fixture.action",
    route: "/federation/devices?section=servers",
  },
  audience: ["local"],
  upgradeOnly: true,
};

/**
 * The registry the tests read: the real `multi-device` notice, then the fixture. Takes the
 * shipped registry rather than importing it, so a test can build it inside the mock of
 * `../registry` itself:
 *
 * ```ts
 * vi.mock("../registry", async (importOriginal) => {
 *   const actual = await importOriginal<typeof import("../registry")>();
 *   const { fixtureRegistry } = await import("./fixtureNotices");
 *
 *   return { ...actual, notices: fixtureRegistry(actual.notices) };
 * });
 * ```
 */
export const fixtureRegistry = (shipped: NoticeDefinition[]): NoticeDefinition[] => {
  const multiDevice = shipped.find((notice) => notice.id === "multi-device");

  if (!multiDevice) throw new Error("The multi-device notice is no longer shipped.");

  return [multiDevice, fixtureRouteNotice];
};
