import { cleanup, render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FileSystemSelectorModal from "../components/Modal";

import { nestedWindowModalClassNames } from "@/components/Window/layers";

const fixture = vi.hoisted(() => ({ modal: vi.fn(), panel: vi.fn() }));

vi.mock("@/components/bakaui", () => ({
  Modal: (props: any) => {
    fixture.modal(props);

    return <div>{props.children}</div>;
  },
}));
vi.mock("@/components/FileSystemSelector", () => ({
  FileSystemSelectorPanel: (props: any) => {
    fixture.panel(props);

    return null;
  },
}));

beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

describe("folder picker overlay ownership", () => {
  it("applies an explicit caller layer to the modal without passing presentation props to its panel", () => {
    render(
      <FileSystemSelectorModal
        modalClassNames={nestedWindowModalClassNames}
        startPath="/local"
        targetType="folder"
      />,
    );
    expect(fixture.modal.mock.lastCall![0].classNames).toBe(nestedWindowModalClassNames);
    expect(fixture.panel.mock.lastCall![0]).toMatchObject({
      startPath: "/local",
      targetType: "folder",
    });
    expect(fixture.panel.mock.lastCall![0]).not.toHaveProperty("modalClassNames");
  });

  it("leaves the normal folder picker on the existing modal layer by default", () => {
    render(<FileSystemSelectorModal targetType="folder" />);
    expect(fixture.modal.mock.lastCall![0].classNames).toBeUndefined();
  });
});
