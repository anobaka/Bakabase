import { describe, expect, it } from "vitest";

import { detectPlatform, playerSchemes, schemesForPlatform } from "../playerSchemes";

const STREAM = "http://192.168.1.5:34567/file/raw?fullname=Z%3A%2Fanime%2Fep1.mkv";
const TITLE = "Episode 1";

const scheme = (id: string) => {
  const found = playerSchemes.find((s) => s.id === id);

  if (!found) throw new Error(`no scheme ${id}`);

  return found;
};

describe("android intents", () => {
  it("uses Chrome's intent: form with the scheme moved into a parameter", () => {
    const link = scheme("vlc-android").build(STREAM, TITLE);

    // A plain custom scheme is refused by Chrome; the intent: form is the
    // documented way to reach an app from a link.
    expect(link.startsWith("intent://192.168.1.5:34567/file/raw?fullname=")).toBe(true);
    expect(link).toContain("#Intent;");
    expect(link).toContain("scheme=http");
    expect(link).toContain("package=org.videolan.vlc");
    expect(link.endsWith(";end")).toBe(true);
  });

  it("keeps the http:// prefix out of the intent authority", () => {
    const link = scheme("mx-android").build(STREAM, TITLE);

    expect(link).not.toContain("intent://http");
  });

  it("percent-encodes the title so a space cannot end the intent early", () => {
    const link = scheme("vlc-android").build(STREAM, "A Movie: Part 2");

    expect(link).toContain("S.title=A%20Movie%3A%20Part%202");
    // A literal ';' inside a value would terminate the intent parameter list.
    expect(link.split("#Intent;")[1]!.split(";").filter((p) => p.startsWith("S.title="))).toHaveLength(1);
  });

  it("omits package for the system chooser entry", () => {
    expect(scheme("chooser-android").build(STREAM, TITLE)).not.toContain("package=");
  });

  it("carries https through when the stream is served over TLS", () => {
    const link = scheme("vlc-android").build("https://host/file/raw?fullname=x", TITLE);

    expect(link).toContain("scheme=https");
    expect(link.startsWith("intent://host/")).toBe(true);
  });
});

describe("ios schemes", () => {
  it("passes the stream to VLC as an encoded x-callback parameter", () => {
    const link = scheme("vlc-ios").build(STREAM, TITLE);

    expect(link.startsWith("vlc-x-callback://x-callback-url/stream?url=")).toBe(true);
    // The stream URL has its own query string, so it must be encoded or it would
    // be parsed as further parameters of the callback URL.
    expect(link).toContain(encodeURIComponent(STREAM));
  });

  it("passes the stream to Infuse as an encoded parameter", () => {
    const link = scheme("infuse-ios").build(STREAM, TITLE);

    expect(link.startsWith("infuse://x-callback-url/play?url=")).toBe(true);
    expect(link).toContain(encodeURIComponent(STREAM));
  });

  it("rewrites the scheme for nPlayer rather than passing a parameter", () => {
    const link = scheme("nplayer-ios").build(STREAM, TITLE);

    expect(link).toBe(`nplayer-${STREAM}`);
    expect(link.startsWith("nplayer-http://")).toBe(true);
  });
});

describe("desktop schemes", () => {
  it("gives IINA only url and title", () => {
    const link = scheme("iina-macos").build(STREAM, TITLE);

    expect(link.startsWith("iina://weblink?url=")).toBe(true);
    expect(link).toContain("title=Episode%201");
    // Anything else used to be forwarded into mpv options, which was exploitable.
    expect(new URL(link.replace("iina://", "https://")).searchParams.size).toBe(2);
  });

  it("marks the handlers that do not exist until the user installs one", () => {
    for (const id of ["potplayer-windows", "vlc-desktop", "mpv-desktop"]) {
      expect(scheme(id).needsSetup, id).toBe(true);
    }

    // IINA is the one desktop player that registers its scheme itself.
    expect(scheme("iina-macos").needsSetup).toBeUndefined();
  });
});

describe("platform selection", () => {
  it("offers only players that exist on the platform", () => {
    for (const platform of ["android", "ios", "windows", "macos", "linux"] as const) {
      const schemes = schemesForPlatform(platform);

      expect(schemes.length, platform).toBeGreaterThan(0);
      expect(schemes.every((s) => s.platforms.includes(platform))).toBe(true);
    }
  });

  it("does not offer android intents to an iPhone", () => {
    expect(schemesForPlatform("ios").map((s) => s.id)).not.toContain("vlc-android");
  });

  it("recognises the common user agents", () => {
    expect(detectPlatform("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)")).toBe("ios");
    expect(detectPlatform("Mozilla/5.0 (Linux; Android 14; Pixel 8)")).toBe("android");
    expect(detectPlatform("Mozilla/5.0 (Windows NT 10.0; Win64; x64)")).toBe("windows");
    expect(detectPlatform("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)")).toBe("macos");
    expect(detectPlatform("Mozilla/5.0 (X11; Linux x86_64)")).toBe("linux");
  });

  it("falls back to unknown rather than guessing", () => {
    expect(detectPlatform("some-crawler/1.0")).toBe("unknown");
  });
});

describe("mpv-handler", () => {
  it("base64url-encodes the target", () => {
    const link = scheme("mpv-desktop").build(STREAM, TITLE);
    const encoded = link.replace("mpv-handler://play/", "");

    // Plain base64 emits '/' and '+', which would break the URL path.
    expect(encoded).not.toMatch(/[/+=]/);
    expect(atob(encoded.replace(/-/g, "+").replace(/_/g, "/"))).toBe(STREAM);
  });
});

describe("every scheme", () => {
  it("produces a link the player can resolve back to the stream", () => {
    for (const s of playerSchemes) {
      const link = s.build(STREAM, TITLE);

      expect(link.length, s.id).toBeGreaterThan(0);

      // The target has to survive somewhere — verbatim, percent-encoded, or
      // base64url'd — or the player has nothing to fetch.
      const carriesTarget =
        link.includes("192.168.1.5:34567") ||
        link.includes(encodeURIComponent(STREAM)) ||
        link.includes(
          btoa(STREAM).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, ""),
        );

      expect(carriesTarget, s.id).toBe(true);
    }
  });
});
