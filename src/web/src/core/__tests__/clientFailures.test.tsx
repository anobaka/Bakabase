import { render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

import { readClientFailure, reportClientFailure } from "../clientFailures";

import i18n from "@/i18n";
import enCommon from "@/locales/en/common.json";
import cnCommon from "@/locales/cn/common.json";
import { ClientForwardingFailure, ClientForwardingFailureLabel } from "@/sdk/constants";

const toast = vi.hoisted(() => ({ warning: vi.fn(), danger: vi.fn() }));

vi.mock("@/components/bakaui", () => ({ toast }));
vi.mock("@/i18n", async () => ({ default: (await import("i18next")).createInstance() }));

const refusal = (failure: string) =>
  new Response(null, { status: 503, headers: { "X-Bakabase-Client": failure } });

describe("the relay's own refusals", () => {
  beforeAll(async () => {
    await i18n.init({
      lng: "en",
      resources: { en: { translation: enCommon }, cn: { translation: cnCommon } },
      interpolation: { escapeValue: false },
    });
  });
  beforeEach(async () => {
    await i18n.changeLanguage("en");
    toast.warning.mockReset();
    toast.danger.mockReset();
  });

  it("reads a server at the address that is not this window's as its own failure", () => {
    expect(readClientFailure(refusal("WrongServer"))).toBe(ClientForwardingFailure.WrongServer);
  });

  it("says another server answers there, in the relay's own words", () => {
    const message =
      "127.0.0.1:34570 now answers as another server (Studio), not Desk. Nothing was sent to it.";

    expect(reportClientFailure(refusal("WrongServer"), { message })).toBe(true);
    expect(toast.danger).toHaveBeenCalledWith({
      title: enCommon["client.failure.wrongServer"],
      description: message,
    });
  });

  it.each([
    ["cn", cnCommon],
    ["en", enCommon],
  ] as const)("shows the server path and mapping link in %s", async (language, words) => {
    await i18n.changeLanguage(language);
    const serverPath = "/Volumes/nas-anobaka/Bakabase/TBD/AV/SNOS-423";
    const body = { serverPath, message: "English relay diagnostic" };

    expect(reportClientFailure(refusal("PathNotMapped"), { error: body })).toBe(true);
    const [notice] = toast.warning.mock.calls[0]!;

    expect(notice.title).toBe(words["client.failure.pathNotMapped"]);
    expect(notice.description).toBe(
      words["client.failure.mapServerPath"].replace("{{path}}", serverPath),
    );
    render(notice.endContent);
    const link = screen.getByRole("link", { name: words["client.failure.addMapping"] });

    expect(link).toHaveAttribute("href", "#/client-path-mapping");
    expect(link).toHaveClass("shrink-0", "whitespace-nowrap");
  });

  it("reports a missing server path without offering to map it", async () => {
    await i18n.changeLanguage("cn");

    reportClientFailure(refusal("PathNotMapped"), { error: { serverPath: null } });
    expect(toast.warning).toHaveBeenCalledWith(cnCommon["client.failure.serverPathMissing"]);
  });

  it("has words for every failure the relay names, in both languages", () => {
    const named = Object.values(ClientForwardingFailureLabel).filter((label) => label !== "None");
    const keyOf: Record<string, string> = {
      NotConnected: "client.failure.notConnected",
      ServerUnreachable: "client.failure.serverUnreachable",
      ForeignCaller: "client.failure.refused",
      NeedsNewerClient: "client.failure.needsNewerClient",
      PathNotMapped: "client.failure.pathNotMapped",
      WrongServer: "client.failure.wrongServer",
    };

    for (const label of named) {
      const key = keyOf[label];

      expect(key, `no words for ${label}`).toBeDefined();
      expect((enCommon as Record<string, string>)[key], key).toBeTruthy();
      expect((cnCommon as Record<string, string>)[key], key).toBeTruthy();
      // The desktop app is what runs the relay; there is no separate client to name.
      expect((enCommon as Record<string, string>)[key]).not.toMatch(/client/i);
      expect((cnCommon as Record<string, string>)[key]).not.toMatch(/客户端/);
    }
  });
});
