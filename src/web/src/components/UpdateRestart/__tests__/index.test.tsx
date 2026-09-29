import React from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import { UpdateRestartOverlay, useRemoteServerUpdateRestart, useUpdateRestart } from "../index";

import { UpdaterStatus } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  restartServer: vi.fn(),
  danger: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { updater: { restartAndUpdateApp: mocks.restartServer } },
}));
vi.mock("@/components/bakaui", () => ({
  Spinner: () => <span>spinner</span>,
  toast: { danger: mocks.danger },
}));

const Fixture = () => {
  const { restart, restarting } = useUpdateRestart();

  return (
    <>
      <button onClick={() => restart()}>restart</button>
      <span>{restarting ? "in progress" : "idle"}</span>
      <UpdateRestartOverlay />
    </>
  );
};

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  vi.useRealTimers();
});

it("guards duplicate restart clicks and restores the UI after a rejected handoff", async () => {
  let rejectFirst!: (reason: Error) => void;
  let rejectSecond!: (reason: Error) => void;

  mocks.restartServer
    .mockImplementationOnce(
      () =>
        new Promise((_resolve, reject) => {
          rejectFirst = reject;
        }),
    )
    .mockImplementationOnce(
      () =>
        new Promise((_resolve, reject) => {
          rejectSecond = reject;
        }),
    );

  render(<Fixture />);

  fireEvent.click(screen.getByRole("button", { name: "restart" }));
  fireEvent.click(screen.getByRole("button", { name: "restart" }));

  expect(mocks.restartServer).toHaveBeenCalledTimes(1);
  expect(mocks.restartServer).toHaveBeenCalledWith({ showErrorToast: false });
  expect(screen.getByText("in progress")).toBeInTheDocument();
  expect(screen.getByText("appUpdate.restarting.detail")).toBeInTheDocument();

  await act(async () => rejectFirst(new Error("preflight failed")));

  expect(screen.getByText("idle")).toBeInTheDocument();
  expect(screen.queryByText("appUpdate.restarting.detail")).not.toBeInTheDocument();
  expect(mocks.danger).toHaveBeenCalledWith({
    title: "appUpdate.restartFailed",
    description: "preflight failed",
  });

  fireEvent.click(screen.getByRole("button", { name: "restart" }));
  expect(mocks.restartServer).toHaveBeenCalledTimes(2);

  await act(async () => rejectSecond(new Error("preflight failed again")));
});

const RemoteFixture = () => {
  const { restart, restarting, timedOut } = useRemoteServerUpdateRestart(
    UpdaterStatus.PendingRestart,
  );

  return (
    <>
      <button onClick={restart}>restart remote</button>
      <span>{restarting ? "waiting for server" : timedOut ? "status unconfirmed" : "idle"}</span>
    </>
  );
};

it("restores remote restart controls when the server never sends another status", async () => {
  vi.useFakeTimers();
  mocks.restartServer.mockResolvedValue({ code: 0 });

  render(<RemoteFixture />);
  fireEvent.click(screen.getByRole("button", { name: "restart remote" }));

  expect(screen.getByText("waiting for server")).toBeInTheDocument();

  act(() => vi.advanceTimersByTime(179_999));
  expect(screen.getByText("waiting for server")).toBeInTheDocument();

  await act(async () => vi.advanceTimersByTime(1));
  expect(screen.getByText("status unconfirmed")).toBeInTheDocument();
});
