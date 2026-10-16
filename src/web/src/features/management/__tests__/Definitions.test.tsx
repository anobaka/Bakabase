import type { ManagementSource } from "../api";

import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { DefinitionGroup } from "../Definitions";

import { PropertyPool, PropertyType } from "@/sdk/constants";

const { clients, propertyEditors } = vi.hoisted(() => ({
  clients: new Map<string, ReturnType<typeof vi.fn>>(),
  propertyEditors: [] as any[],
}));

vi.mock("../api", () => ({
  ownerClient: (source: ManagementSource) => clients.get(source.serverId),
}));
vi.mock("@/components/Property/components/PropertyTypeIcon", () => ({ default: () => null }));
vi.mock("@/components/PropertyModal", () => ({
  default: (props: any) => {
    propertyEditors.push(props);

    return <div>Property editor</div>;
  },
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  Spinner: () => <span>Loading</span>,
  Input: ({ label, value, onValueChange }: any) => (
    <input aria-label={label} value={value} onChange={(e) => onValueChange(e.target.value)} />
  ),
  Modal: ({ title, children, onOk }: any) => (
    <section aria-label={title}>
      {children}
      <button onClick={onOk}>Save</button>
    </section>
  ),
}));

const source = (serverId: string): ManagementSource => ({
  serverId,
  name: serverId,
  libraryEpoch: `epoch-${serverId}`,
  available: true,
  isLocal: false,
  contractVersion: 1,
});
const property = { id: 7, name: "Category", type: PropertyType.SingleChoice };

beforeEach(() => {
  clients.clear();
  propertyEditors.length = 0;
});
afterEach(cleanup);

describe("owner-scoped definition editors", () => {
  it("retains path newlines while typing, filtering empty rows only on save to the owner", async () => {
    const client = vi.fn(async () => ({ code: 0, data: [] }));

    clients.set("b", client);
    render(<DefinitionGroup keyword="" kind="libraries" source={source("b")} />);
    await waitFor(() => expect(client).toHaveBeenCalledWith("libraries"));
    fireEvent.click(screen.getByText("management.create"));
    fireEvent.change(screen.getByLabelText("management.name"), { target: { value: "Movies" } });
    const paths = screen.getByLabelText("management.libraryPaths");

    fireEvent.change(paths, { target: { value: "/owner/movies\n" } });
    expect(paths).toHaveValue("/owner/movies\n");
    fireEvent.change(paths, { target: { value: "/owner/movies\n/owner/series\n" } });
    fireEvent.click(screen.getByText("Save"));
    await waitFor(() =>
      expect(client).toHaveBeenCalledWith("libraryAdd", {
        id: undefined,
        body: { name: "Movies", color: undefined, paths: ["/owner/movies", "/owner/series"] },
      }),
    );
  });

  it("keeps equal-numbered property definitions and option usage checks on their owner", async () => {
    const a = vi.fn(async (_operation: string) => ({ data: [property] }));
    const b = vi.fn(async (operation: string) =>
      operation === "propertyValueResourceCounts"
        ? { data: { isReady: true, counts: { selected: 12 } } }
        : { data: operation === "customProperties" ? [property] : property },
    );

    clients.set("a", a);
    clients.set("b", b);
    render(
      <>
        <DefinitionGroup keyword="" kind="properties" source={source("a")} />
        <DefinitionGroup keyword="" kind="properties" source={source("b")} />
      </>,
    );
    await waitFor(() => expect(screen.getAllByText("Category")).toHaveLength(2));
    const bSection = screen.getByText("b").closest("section")!;

    fireEvent.click(within(bSection).getByText("management.edit"));
    const editor = propertyEditors.at(-1);

    expect(editor.ownerScoped).toBe(true);
    expect(await editor.checkOptionUsage(7, "selected")).toBe(12);
    expect(b).toHaveBeenCalledWith("propertyValueResourceCounts", {
      id: 7,
      query: { pool: [String(PropertyPool.Custom)] },
      body: { page: 1, pageSize: 100 },
    });
    await editor.saveProperty({ ...property, name: "New category", options: "{}" });
    expect(b).toHaveBeenCalledWith("customPropertyPut", {
      id: 7,
      body: { ...property, name: "New category", options: "{}" },
    });
    expect(a.mock.calls.every(([operation]) => operation === "customProperties")).toBe(true);
  });

  it("blocks unsafe option removal when the owner's usage index is unavailable", async () => {
    const client = vi.fn(async (operation: string) => ({
      data: operation === "customProperties" ? [property] : { isReady: false, counts: {} },
    }));

    clients.set("b", client);
    render(<DefinitionGroup keyword="" kind="properties" source={source("b")} />);
    await waitFor(() => expect(screen.getByText("Category")).toBeTruthy());
    fireEvent.click(screen.getByText("management.edit"));
    await expect(propertyEditors.at(-1).checkOptionUsage(7, "selected")).rejects.toThrow(
      "property.editor.usageUnavailable",
    );
  });
});
