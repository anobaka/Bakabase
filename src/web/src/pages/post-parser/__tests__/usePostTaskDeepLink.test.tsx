import type { ReactNode } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, useNavigate } from "react-router-dom";
import { expect, it, vi } from "vitest";
import usePostTaskDeepLink, { postTaskIdFromSearch } from "../usePostTaskDeepLink";

it("accepts only an exact positive server task ID", () => {
  expect(postTaskIdFromSearch("?taskId=411")).toBe(411);
  for (const value of ["", "0", "-1", "411x", "4.1", "001", "2147483648", "Infinity", "1e2"])
    expect(postTaskIdFromSearch("?taskId=" + value)).toBeUndefined();
});

it("locates once per navigation and never invokes a parsing or execution action", () => {
  const locate = vi.fn();
  function Page({ children }: { children?: ReactNode }) {
    usePostTaskDeepLink(locate);
    const navigate = useNavigate();
    return (
      <>
        <button onClick={() => navigate("/post-parser?taskId=412")}>next source</button>
        {children}
      </>
    );
  }
  const view = render(
    <MemoryRouter initialEntries={["/post-parser?taskId=411"]}>
      <Page />
    </MemoryRouter>,
  );
  expect(locate).toHaveBeenCalledExactlyOnceWith(411);
  view.rerender(
    <MemoryRouter initialEntries={["/post-parser?taskId=411"]}>
      <Page>unrelated render</Page>
    </MemoryRouter>,
  );
  expect(locate).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByRole("button", { name: "next source" }));
  expect(locate).toHaveBeenCalledTimes(2);
  expect(locate).toHaveBeenLastCalledWith(412);
});
