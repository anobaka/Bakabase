import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { BakabaseContext, useBakabaseContext } from "../BakabaseContextProvider";

import {
  ResourceOwnerProvider,
  useResourceOwnerScope,
} from "@/features/management/resourceContext";
import type { ManagementSource } from "@/features/management/api";

vi.mock("@/components/SignalR/UIHubConnection", () => ({ UIHubConnection: () => null }));
vi.mock("@/services/Analytics", () => ({
  initAnalytics: vi.fn(),
  trackAnalyticsEvent: vi.fn(),
  stopAnalytics: vi.fn(),
}));
vi.mock("@/components/Window", () => ({ default: () => null }));
vi.mock("@/components/Error", () => ({
  ErrorBoundary: ({ children }: { children: ReactNode }) => children,
}));

const source = (serverId: string): ManagementSource => ({
  serverId,
  name: serverId,
  libraryEpoch: `epoch-${serverId}`,
  available: true,
  isLocal: false,
  contractVersion: 1,
});

function CapturedEditor({ nested = false }: { nested?: boolean; onDestroyed?: () => void }) {
  const scope = useResourceOwnerScope();
  const { createPortal } = useBakabaseContext();

  return (
    <div>
      <span data-testid={nested ? "nested-owner" : "editor-owner"}>
        {JSON.stringify([scope?.source.serverId, scope?.source.libraryEpoch, scope?.resourceId])}
      </span>
      {!nested && (
        <button onClick={() => createPortal(CapturedEditor, { nested: true })}>nested</button>
      )}
    </div>
  );
}
function Trigger() {
  const { createPortal, createWindow } = useBakabaseContext();

  return (
    <>
      <button onClick={() => createPortal(CapturedEditor, {})}>edit</button>
      <button onClick={() => createWindow(CapturedEditor, {})}>window</button>
    </>
  );
}
function Host() {
  const [owner, setOwner] = useState(() => source("one"));
  const [portals, setPortals] = useState<ReactNode[]>([]);
  const mount = (Component: any, props: any) => {
    setPortals((previous) => [...previous, <Component key={previous.length} {...props} />]);

    return { key: "test", destroy: () => {} };
  };

  return (
    <BakabaseContext.Provider
      value={{
        createPortal: mount,
        createWindow: mount,
        closeAllModals: () => {},
        isDarkMode: false,
      }}
    >
      {portals}
      <button onClick={() => setOwner(source("two"))}>switch</button>
      <ResourceOwnerProvider source={owner} resourceId={7}>
        <Trigger />
      </ResourceOwnerProvider>
    </BakabaseContext.Provider>
  );
}

afterEach(cleanup);
describe("owner capture through root-mounted modals and windows", () => {
  it.each(["edit", "window"])(
    "keeps %s and a nested editor bound to the opening device after a filter changes",
    async (action) => {
      render(<Host />);
      await act(async () => fireEvent.click(screen.getByText(action)));
      await act(async () => fireEvent.click(screen.getByText("switch")));
      await act(async () => fireEvent.click(screen.getByText("nested")));
      expect(screen.getByTestId("editor-owner").textContent).toBe('["one","epoch-one",7]');
      expect(screen.getByTestId("nested-owner").textContent).toBe('["one","epoch-one",7]');
    },
  );
});
