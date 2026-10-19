import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { describe, expect, it } from "vitest";
import BtDownloadsPage from "..";

function Destination() {
  const location = useLocation();
  return (
    <div>
      {location.pathname}
      {location.search}
    </div>
  );
}
describe("BT download route compatibility", () => {
  it.each(["/bt-downloads", "/bt-downloads?id=torrent-1"])(
    "redirects %s to the unified list",
    (path) => {
      render(
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/bt-downloads" element={<BtDownloadsPage />} />
            <Route path="/downloader" element={<Destination />} />
          </Routes>
        </MemoryRouter>,
      );
      const route = screen.getByText(/\/downloader\?/).textContent!;
      expect(route).toContain("category=general");
      expect(route).toContain("provider=bt");
      if (path.includes("id=")) expect(route).toContain("id=bt%3Atorrent-1");
    },
  );
});
