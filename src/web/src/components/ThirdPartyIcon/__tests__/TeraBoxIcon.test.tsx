import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import ThirdPartyIcon from "..";
import { ThirdPartyId } from "@/sdk/constants";
import officialLogo from "@/assets/logo/terabox.png";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/i18n", () => ({ getEnumKey: (name: string, value: string) => `${name}.${value}` }));

it("uses the bundled official TeraBox asset with an accessible service name", () => {
  render(<ThirdPartyIcon thirdPartyId={ThirdPartyId.TeraBox} size="md" />);
  expect(screen.getByRole("img", { name: "ThirdPartyId.TeraBox" })).toHaveAttribute("src", officialLogo);
  expect(screen.getByRole("img")).toHaveStyle({ width: "24px", height: "24px" });
});
