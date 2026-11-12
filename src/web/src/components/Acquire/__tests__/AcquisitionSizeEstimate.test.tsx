import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import AcquisitionSizeEstimate from "../AcquisitionSizeEstimate";
vi.mock("@/components/bakaui", () => ({ Tooltip: ({ children }: any) => children }));
vi.mock("@/components/utils", () => ({ bytesToSize: (bytes: number) => `${bytes} bytes` }));

it("identifies downloaded input as a reference and marks partial estimates", () => {
  const view = render(
    <AcquisitionSizeEstimate
      estimate={{ bytes: 1024, basis: "downloadReference", partial: true }}
    />,
  );
  expect(
    screen.getByLabelText(
      "acquisition.size.value.downloadReference. acquisition.size.basis.downloadReference acquisition.size.partial.hint",
    ),
  ).toHaveTextContent("acquisition.size.partial.label");
  view.rerender(<AcquisitionSizeEstimate estimate={{ basis: "unknown", partial: false }} />);
  expect(screen.getByText("acquisition.size.unknown")).toBeVisible();
  expect(screen.queryByText("acquisition.size.partial.label")).toBeNull();
});

it.each(["delivered", "processed"])(
  "preserves the %s basis and accepts a known zero-byte size",
  (basis) => {
    render(<AcquisitionSizeEstimate estimate={{ bytes: 0, basis, partial: false }} />);
    expect(
      screen.getByLabelText(`acquisition.size.value.${basis}. acquisition.size.basis.${basis}`),
    ).toHaveTextContent(`acquisition.size.value.${basis}`);
  },
);
