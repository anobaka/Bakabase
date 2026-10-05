import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { HeroUIProvider } from "@heroui/react";
import { describe, expect, it, vi } from "vitest";

import { AcquisitionWaitForInboxUI } from "../Activities/AcquisitionWaitForInbox";
import { AcquisitionUnpackUI } from "../Activities/AcquisitionUnpack";
import { AcquisitionResolveSharedContentUI } from "../Activities/AcquisitionResolveSharedContent";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: { name?: string }) => (values?.name ? `${key} ${values.name}` : key),
  }),
}));
vi.mock("@/components/bakaui", async () => ({ ...(await import("@heroui/react")) }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      getAcquisitionInbox: vi.fn().mockResolvedValue({
        data: [
          { path: "/inbox/a.001", fileName: "a.001", isStable: true },
          { path: "/inbox/a.002", fileName: "a.002", isStable: true },
          { path: "/inbox/incomplete", fileName: "incomplete", isStable: false },
        ],
      }),
    },
  },
}));

describe("manual file delivery", () => {
  it("shows purchase risk and preserves the reserve in the shared-content workflow", () => {
    const submit = vi.fn();
    const Form = AcquisitionResolveSharedContentUI.ResumeForm!;

    render(
      <HeroUIProvider>
        <Form
          promptJson={JSON.stringify({
            locked: [{ url: "https://post.test/buy", price: 8 }],
            limit: 0,
            balance: 10,
            minimumRemainingCoins: 5,
            availability: { status: "expired", evidence: ["The link is gone"] },
            message: "Needs review",
          })}
          submitting={false}
          onSubmit={submit}
        />
      </HeroUIProvider>,
    );
    expect(screen.getByText("Needs review")).toBeInTheDocument();
    expect(
      screen.getByRole("button", {
        name: "workflow.acquisition.resolveSharedContent.purchase.approve",
      }),
    ).toBeDisabled();
    fireEvent.click(
      screen.getByRole("button", {
        name: "workflow.acquisition.resolveSharedContent.purchase.decline",
      }),
    );
    expect(JSON.parse(JSON.parse(submit.mock.calls[0][0]).payloadJson)).toEqual({
      approved: false,
    });
  });
  it("delivers all selected archive volumes together and records prior manual processing", async () => {
    const submit = vi.fn();
    const Form = AcquisitionWaitForInboxUI.ResumeForm!;

    render(
      <HeroUIProvider>
        <Form promptJson={null} submitting={false} onSubmit={submit} />
      </HeroUIProvider>,
    );
    await waitFor(() =>
      expect(
        screen.getByRole("checkbox", { name: "workflow.processing.selectFile a.001" }),
      ).toBeInTheDocument(),
    );
    fireEvent.click(screen.getByRole("checkbox", { name: "workflow.processing.selectFile a.001" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "workflow.processing.selectFile a.002" }));
    expect(
      screen.getByRole("checkbox", { name: "workflow.processing.selectFile incomplete" }),
    ).toBeDisabled();
    fireEvent.click(screen.getByRole("switch", { name: "workflow.processing.alreadyProcessed" }));
    fireEvent.click(screen.getByRole("button", { name: "workflow.processing.claimSelection" }));
    const signal = JSON.parse(submit.mock.calls[0][0]);

    expect(JSON.parse(signal.payloadJson)).toEqual({
      files: ["/inbox/a.001", "/inbox/a.002"],
      alreadyProcessed: true,
    });
  });

  it("requires a processing plan instead of claiming manual output without delivering files", () => {
    const submit = vi.fn();
    const Form = AcquisitionUnpackUI.ResumeForm!;

    render(
      <HeroUIProvider>
        <Form
          promptJson={JSON.stringify({
            reason: "ExtractionPlanUnknown",
            message: "Missing plan",
            extractionPlanJson: '{"requirement":"unknown","steps":[]}',
          })}
          submitting={false}
          onSubmit={submit}
        />
      </HeroUIProvider>,
    );
    const confirm = screen.getByRole("button", { name: "workflow.acquisition.unpack.tryIt" });

    expect(confirm).toBeDisabled();
    expect(
      screen.queryByRole("switch", { name: "workflow.processing.alreadyProcessed" }),
    ).not.toBeInTheDocument();
    fireEvent.change(screen.getByRole("textbox", { name: "workflow.processing.plan" }), {
      target: { value: '{"requirement":"notRequired","steps":[]}' },
    });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    const signal = JSON.parse(submit.mock.calls[0][0]);

    expect(signal.reason).toBe(10);
    expect(JSON.parse(signal.payloadJson)).toEqual({
      password: "",
      extractionPlanJson: '{"requirement":"notRequired","steps":[]}',
    });
  });
});
