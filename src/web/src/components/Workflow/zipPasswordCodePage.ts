import { zipPasswordCodePages, type ZipPasswordCodePage } from "@/sdk/constants";

export function isZipPasswordCodePage(value: unknown): value is ZipPasswordCodePage {
  return zipPasswordCodePages.some((choice) => choice.value === value);
}
