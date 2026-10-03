import { HeroUIProvider } from "@heroui/react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import OtherDevicesPage from "..";

const api = vi.hoisted(() => ({ downloads: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { otherDevices: { getOtherDeviceDownloads: api.downloads } },
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: { href: string; children: React.ReactNode }) => (
    <a href={href}>{children}</a>
  ),
}));
vi.mock("qrcode.react", () => ({
  QRCodeSVG: ({ value, title }: { value: string; title: string }) => (
    <svg aria-label={title} data-qr-value={value} role="img" />
  ),
}));

const manifest = {
  version: "2.4.0",
  publishedAt: "2026-10-02T08:00:00Z",
  releaseUrl: "https://github.com/anobaka/Bakabase/releases/tag/mobile-v2.4.0",
  sidestoreSourceUrl: "https://downloads.example/source.json",
  files: [
    {
      name: "phone.apk",
      platform: "android-arm64-v8a",
      size: 1048576,
      cdnUrl: "https://downloads.example/actual-phone.apk",
      githubUrl: "https://github.com/example/phone.apk",
    },
    {
      name: "other.apk",
      platform: "android-x86_64",
      githubUrl: "https://github.com/example/other.apk",
    },
    { name: "phone.ipa", platform: "ios", cdnUrl: "https://downloads.example/actual-phone.ipa" },
  ],
};

const mount = () =>
  render(
    <HeroUIProvider>
      <OtherDevicesPage />
    </HeroUIProvider>,
  );

beforeEach(() => {
  api.downloads.mockReset();
});
afterEach(cleanup);

describe("downloads and deployment", () => {
  it("keeps desktop and Docker available while mobile downloads load", async () => {
    let finish!: (value: unknown) => void;

    api.downloads.mockReturnValue(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    mount();

    expect(screen.getAllByRole("region")).toHaveLength(3);
    expect(screen.getByRole("link", { name: "otherDevices.desktop.stable" })).toHaveAttribute(
      "href",
      "https://github.com/anobaka/Bakabase/releases/latest",
    );
    expect(screen.getByRole("link", { name: "Docker Hub" })).toHaveAttribute(
      "href",
      "https://hub.docker.com/r/anobaka/bakabase",
    );
    expect(screen.getByRole("status")).toHaveTextContent("otherDevices.loading");
    expect(screen.getByText("otherDevices.mobile.noSharing")).toBeVisible();

    finish({ code: 0, data: { mobile: null } });
    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("otherDevices.unavailable"),
    );
  });

  it("recovers a failed mobile fetch through the retry control", async () => {
    api.downloads.mockRejectedValueOnce(new Error("offline"));
    api.downloads.mockResolvedValueOnce({ code: 0, data: { mobile: manifest } });
    mount();
    fireEvent.click(await screen.findByRole("button", { name: "otherDevices.retry" }));

    expect(await screen.findByText("v2.4.0")).toBeVisible();
    expect(api.downloads).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("uses exact manifest links for packages and QR codes", async () => {
    api.downloads.mockResolvedValue({ code: 0, data: { mobile: manifest } });
    mount();
    await screen.findByText("v2.4.0");

    const links = screen.getAllByRole("link").map((link) => link.getAttribute("href"));

    expect(links).toEqual(
      expect.arrayContaining([
        manifest.releaseUrl,
        manifest.sidestoreSourceUrl,
        ...manifest.files.flatMap((file) => [file.cdnUrl, file.githubUrl].filter(Boolean)),
      ]),
    );
    expect(screen.getByRole("img", { name: "Android" })).toHaveAttribute(
      "data-qr-value",
      manifest.files[0].cdnUrl,
    );
    expect(screen.getByRole("img", { name: "iOS" })).toHaveAttribute(
      "data-qr-value",
      manifest.sidestoreSourceUrl,
    );
    expect(screen.getByText("1.0 MB")).toBeVisible();
  });

  it("offers GitHub QR fallback and direct IPA guidance when sources are absent", async () => {
    api.downloads.mockResolvedValue({
      code: 0,
      data: {
        mobile: {
          ...manifest,
          sidestoreSourceUrl: null,
          files: [
            {
              name: "phone.apk",
              platform: "android-arm64-v8a",
              githubUrl: "https://github.com/example/phone.apk",
            },
            {
              name: "phone.ipa",
              platform: "ios",
              githubUrl: "https://github.com/example/phone.ipa",
            },
          ],
        },
      },
    });
    mount();
    await screen.findByText("v2.4.0");

    expect(screen.getByRole("img", { name: "Android" })).toHaveAttribute(
      "data-qr-value",
      "https://github.com/example/phone.apk",
    );
    expect(screen.getByRole("img", { name: "iOS" })).toHaveAttribute(
      "data-qr-value",
      "https://github.com/example/phone.ipa",
    );
    expect(screen.getByText("otherDevices.mobile.iosIpaHint")).toBeVisible();
    expect(screen.queryByText("otherDevices.mobile.iosSourceHint")).not.toBeInTheDocument();
  });

  it("treats API errors as unavailable and explains platforms without packages", async () => {
    api.downloads.mockResolvedValueOnce({ code: 1, data: { mobile: manifest } });
    api.downloads.mockResolvedValueOnce({
      code: 0,
      data: { mobile: { ...manifest, files: [], sidestoreSourceUrl: null } },
    });
    mount();
    fireEvent.click(await screen.findByRole("button", { name: "otherDevices.retry" }));

    await screen.findByText("v2.4.0");
    expect(screen.getAllByText("otherDevices.mobile.noPackage")).toHaveLength(2);
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("provides expandable installation instructions and the complete Docker guide", async () => {
    api.downloads.mockResolvedValue({ code: 0, data: {} });
    mount();
    await screen.findByRole("status");
    const summary = screen.getByText("otherDevices.docker.instructions");
    const details = summary.closest("details")!;

    expect(details).not.toHaveAttribute("open");
    fireEvent.click(summary);
    expect(details).toHaveAttribute("open");
    expect(screen.getByText(/API_LISTENING_PORTS=34567/)).toBeVisible();
    expect(screen.getByRole("link", { name: "otherDevices.docker.guide" })).toHaveAttribute(
      "href",
      "https://github.com/anobaka/Bakabase/blob/main/docs/docker-installation.md",
    );
  });
});
