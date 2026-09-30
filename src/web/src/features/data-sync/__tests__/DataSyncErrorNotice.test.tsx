import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { DataSyncProblemError } from "../api";
import {
  DataSyncConfirmDialog,
  DataSyncErrorNotice,
  taskFailureText,
  wordedProblemDetails,
} from "../components/common";

import { bTask, keyT } from "./dataSyncFixtures";

import { BTaskStatus, DataSyncProblemCode } from "@/sdk/constants";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string) => key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => true },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));

afterEach(cleanup);

const problem = (code: DataSyncProblemCode, detail?: string) =>
  new DataSyncProblemError({ code, detail });

describe("a problem the server answered", () => {
  it("is said in the problem's words, its detail kept for whoever looks into it", () => {
    render(
      <DataSyncErrorNotice
        error={problem(DataSyncProblemCode.DecisionsInvalid, "notApplicable:askAccessAgain")}
      />,
    );
    const notice = screen.getByTestId("data-sync-error");
    const technical = screen.getByTestId("data-sync-error-technical");

    expect(notice.querySelector("p")).toHaveTextContent("dataSync.problem.DecisionsInvalid");
    expect(notice.querySelector("p")).not.toHaveTextContent("notApplicable");
    // Collapsed, under "Technical details".
    expect(technical.tagName).toBe("DETAILS");
    expect(technical).not.toHaveAttribute("open");
    expect(technical).toHaveTextContent("dataSync.error.technical");
    expect(technical).toHaveTextContent("notApplicable:askAccessAgain");
  });

  it("is said in the words of a detail that changes what it means, and only those", () => {
    render(
      <DataSyncErrorNotice error={problem(DataSyncProblemCode.DecisionsInvalid, "noRestore")} />,
    );

    expect(screen.getByTestId("data-sync-error")).toHaveTextContent(
      "dataSync.problem.detail.DecisionsInvalid.noRestore",
    );
    expect(screen.getByTestId("data-sync-error")).not.toHaveTextContent(
      "dataSync.problem.DecisionsInvalid",
    );
    expect(screen.queryByTestId("data-sync-error-technical")).toBeNull();
  });

  it("names only problem codes the server has", () => {
    for (const code of Object.keys(wordedProblemDetails))
      expect(DataSyncProblemCode[code as keyof typeof DataSyncProblemCode], code).toEqual(
        expect.any(Number),
      );
  });

  it("is said in data sync's words in the multi-device confirmation", () => {
    render(
      <DataSyncConfirmDialog
        busy={false}
        description="description"
        error={problem(DataSyncProblemCode.ApplyInProgress, "DataSyncApply")}
        title="title"
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("dataSync.problem.ApplyInProgress");
    expect(dialog).not.toHaveTextContent("federation.error.network");
  });
});

describe("a task that failed", () => {
  it("says the problem it ended with, else the fallback — never its stack trace", () => {
    const failed = (briefError?: string) =>
      bTask("DataSyncRestore", BTaskStatus.Error, "2026-09-01 08:00:00.000", {
        briefError,
        error: "System.Exception: boom\n   at …",
      });

    expect(taskFailureText(keyT, failed("BackupFailed"), "fallback")).toBe(
      "dataSync.problem.BackupFailed",
    );
    expect(taskFailureText(keyT, failed("ActorUnverified"), "fallback")).toBe("fallback");
    expect(taskFailureText(keyT, failed(), "fallback")).toBe("fallback");
    expect(taskFailureText(keyT, undefined, "fallback")).toBe("fallback");
  });
});
